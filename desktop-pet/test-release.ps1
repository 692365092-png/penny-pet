#requires -Version 5.1
param(
    [Parameter(Mandatory = $true)][string]$Executable,
    [Parameter(Mandatory = $true)][string]$ExpectedVersion,
    [Parameter(Mandatory = $true)][string]$ReportPath
)

$ErrorActionPreference = "Stop"
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
using System.Text;

public static class PennyReleaseWindow
{
    private delegate bool EnumWindow(IntPtr window, IntPtr state);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindow callback, IntPtr state);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr window, StringBuilder text, int count);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr window, StringBuilder text, int count);
    [DllImport("user32.dll")] private static extern IntPtr SendMessageTimeout(IntPtr window, uint message, IntPtr wParam, IntPtr lParam, uint flags, uint timeout, out IntPtr result);
    [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);

    public static IntPtr Find(int processId, string title)
    {
        IntPtr found = IntPtr.Zero;
        EnumWindows(delegate(IntPtr window, IntPtr state)
        {
            uint owner;
            GetWindowThreadProcessId(window, out owner);
            if (owner != processId || !IsWindowVisible(window)) return true;
            StringBuilder name = new StringBuilder(256);
            GetWindowText(window, name, name.Capacity);
            if (name.ToString() != title) return true;
            GetClassName(window, name, name.Capacity);
            // Do not mistake a startup error MessageBox for the actual pet.
            if (!name.ToString().StartsWith("WindowsForms10.", StringComparison.Ordinal)) return true;
            found = window;
            return false;
        }, IntPtr.Zero);
        return found;
    }

    public static bool Responds(IntPtr window)
    {
        IntPtr result;
        return SendMessageTimeout(window, 0, IntPtr.Zero, IntPtr.Zero,
            0x22, 1000, out result) != IntPtr.Zero;
    }
}
'@

$Executable = (Resolve-Path -LiteralPath $Executable).Path
$ReportPath = [IO.Path]::GetFullPath($ReportPath)
$stage = Join-Path ([IO.Path]::GetTempPath()) ("penny-release-smoke-" + [Guid]::NewGuid())
$process = $null
$result = [ordered]@{ ok = $false; version = $ExpectedVersion; resources = @(); window = $false; exited = $false }
try {
    $version = [Diagnostics.FileVersionInfo]::GetVersionInfo($Executable)
    if ($version.FileVersion -ne $ExpectedVersion -or
        $version.ProductVersion -ne $ExpectedVersion) {
        throw "Release version does not match ProductVersion.props."
    }
    $assembly = [Reflection.Assembly]::ReflectionOnlyLoadFrom($Executable)
    foreach ($name in @("PennyPet.SelfTest", "PennyPet.SelfTestCommandRouter",
        "PennyPet.ArtCommandRouter", "PennyPet.CommandLineArguments")) {
        if ($null -ne $assembly.GetType($name, $false)) {
            throw "Release contains a test/tool entry point: $name"
        }
    }
    if (@($assembly.GetManifestResourceNames() | Where-Object { $_ -like "PennyPet.Tests.*" }).Count -ne 0) {
        throw "Release contains test fixtures."
    }
    $required = @(
        "PennyPet.Art.Manifest", "PennyPet.Art.ReleasePack",
        "PennyPet.Art.StartupCache", "PennyPet.Startup.Loading",
        "PennyPet.ContactAuthor.Image", "PennyPet.TabIcons.Reference",
        "PennyPet.Dependencies.astronomy.dll", "PennyPet.Dependencies.lunar.dll"
    )
    foreach ($name in $required) {
        $stream = $assembly.GetManifestResourceStream($name)
        if ($null -eq $stream) { throw "Missing embedded resource: $name" }
        try {
            if ($stream.Length -eq 0) { throw "Empty embedded resource: $name" }
        } finally { $stream.Dispose() }
    }
    $result.resources = $required
    $reader = New-Object IO.StreamReader($assembly.GetManifestResourceStream("PennyPet.Art.Manifest"))
    try { $title = ($reader.ReadToEnd() | ConvertFrom-Json).displayName }
    finally { $reader.Dispose() }
    if ([string]::IsNullOrWhiteSpace($title)) { throw "Art manifest has no display name." }

    # Run only the distributed file, without adjacent source art or DLLs.
    New-Item -ItemType Directory -Path $stage | Out-Null
    $stagedExe = Join-Path $stage "Penny-pet-Windows.exe"
    Copy-Item -LiteralPath $Executable -Destination $stagedExe
    $process = Start-Process -FilePath $stagedExe -WorkingDirectory $stage -PassThru
    $deadline = [DateTime]::UtcNow.AddSeconds(45)
    $window = [IntPtr]::Zero
    while ([DateTime]::UtcNow -lt $deadline) {
        if ($process.HasExited) { throw "Release exited before showing the pet (exit $($process.ExitCode))." }
        $window = [PennyReleaseWindow]::Find($process.Id, $title)
        if ($window -ne [IntPtr]::Zero -and [PennyReleaseWindow]::Responds($window)) { break }
        Start-Sleep -Milliseconds 100
    }
    if ($window -eq [IntPtr]::Zero -or -not [PennyReleaseWindow]::Responds($window)) {
        throw "Release did not show a responsive pet window."
    }
    $result.window = $true
    if (-not [PennyReleaseWindow]::PostMessage($window, 0x0010, [IntPtr]::Zero, [IntPtr]::Zero)) {
        throw "Could not request normal window close."
    }
    if (-not $process.WaitForExit(30000)) { throw "Release did not finish normal shutdown." }
    if ($process.ExitCode -ne 0) { throw "Release failed during shutdown (exit $($process.ExitCode))." }
    $result.exited = $true
    $result.ok = $true
} catch {
    $result.error = $_.Exception.Message
    throw
} finally {
    if ($null -ne $process) {
        if (-not $process.HasExited) { $process.Kill(); $process.WaitForExit() }
        $process.Dispose()
    }
    if (Test-Path -LiteralPath $stage) { Remove-Item -LiteralPath $stage -Recurse -Force }
    [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($ReportPath)) | Out-Null
    $result | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $ReportPath -Encoding UTF8
}
