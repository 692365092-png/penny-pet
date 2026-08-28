param(
    [ValidateSet("anycpu", "x86", "x64")]
    [string]$TargetPlatform = "anycpu",
    [string]$OutputFile = ""
)

$ErrorActionPreference = "Stop"

$ProjectRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$WorkspaceRoot = Split-Path -Parent $ProjectRoot
$VersionPropertiesPath = Join-Path $ProjectRoot "ProductVersion.props"
[xml]$VersionProperties = Get-Content -LiteralPath $VersionPropertiesPath -Raw
$VersionPrefixNode = $VersionProperties.SelectSingleNode(
    "/Project/PropertyGroup/PennyVersionPrefix")
$PennyVersionPrefix = if ($null -eq $VersionPrefixNode) {
    ""
} else { $VersionPrefixNode.InnerText.Trim() }
if ($PennyVersionPrefix -notmatch '^\d+\.\d+\.\d+$') {
    throw "ProductVersion.props contains an invalid version prefix."
}
$PennyProductVersion = $PennyVersionPrefix
$PennyAssemblyVersion = $PennyVersionPrefix + ".0"
$ProtectedFileName = "Penny-" + $PennyProductVersion + ".exe"
$FinalOutput = if ([String]::IsNullOrWhiteSpace($OutputFile)) {
    Join-Path $WorkspaceRoot $ProtectedFileName
} else {
    [IO.Path]::GetFullPath($OutputFile)
}
$ConfuserVersion = "1.6.0"
$ConfuserDownload =
    "https://github.com/mkaring/ConfuserEx/releases/download/v1.6.0/ConfuserEx-CLI.zip"
$ConfuserZipSha256 =
    "A00DE7CDDC740F7EDB1BAAB4C6C9073553DCC88F7E873D15B7FD34DDD33753D7"
$LocalAppDataRoot = [Environment]::GetFolderPath(
    [Environment+SpecialFolder]::LocalApplicationData)
$ToolRoot = Join-Path $LocalAppDataRoot `
    "PennyPetBuildTools\ConfuserEx\$ConfuserVersion"
$ConfuserCli = Join-Path $ToolRoot "Confuser.CLI.exe"
$TemporaryParent = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\')
$BuildRoot = Join-Path $TemporaryParent (
    "penny-protected-build-" + [Guid]::NewGuid().ToString("N"))
$InputRoot = Join-Path $BuildRoot "input"
$ProtectedRoot = Join-Path $BuildRoot "protected"
$RawExe = Join-Path $InputRoot $ProtectedFileName
$ProtectedExe = Join-Path $ProtectedRoot $ProtectedFileName
$ProjectFile = Join-Path $BuildRoot "penny.crproj"
$SelfTestFile = Join-Path $BuildRoot "protected-selftest.json"
$StartupProbeFile = Join-Path $BuildRoot "protected-startup.json"

function Ensure-ConfuserEx {
    if (Test-Path -LiteralPath $ConfuserCli -PathType Leaf) { return }
    New-Item -ItemType Directory -Force -Path $ToolRoot | Out-Null
    $zipPath = Join-Path $BuildRoot "ConfuserEx-CLI.zip"
    Invoke-WebRequest -Uri $ConfuserDownload -OutFile $zipPath -Headers @{
        "User-Agent" = "PennyPet-Protected-Build"
    }
    $actualHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $zipPath).Hash
    if (-not [String]::Equals($actualHash, $ConfuserZipSha256,
        [StringComparison]::OrdinalIgnoreCase)) {
        throw "ConfuserEx download checksum mismatch."
    }
    Expand-Archive -LiteralPath $zipPath -DestinationPath $ToolRoot -Force
    if (-not (Test-Path -LiteralPath $ConfuserCli -PathType Leaf)) {
        throw "ConfuserEx CLI was not found after extraction."
    }
}

function Wait-ForFile([string]$Path, [int]$Seconds) {
    $deadline = [DateTime]::UtcNow.AddSeconds($Seconds)
    while (-not (Test-Path -LiteralPath $Path -PathType Leaf) -and
        [DateTime]::UtcNow -lt $deadline) {
        Start-Sleep -Milliseconds 100
    }
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "Timed out waiting for: $Path"
    }
}

New-Item -ItemType Directory -Force -Path $InputRoot, $ProtectedRoot | Out-Null
try {
    Ensure-ConfuserEx

    & (Join-Path $ProjectRoot "build.ps1") -TargetPlatform $TargetPlatform `
        -OutputFile $RawExe
    if (-not (Test-Path -LiteralPath $RawExe -PathType Leaf)) {
        throw "The unprotected build was not created."
    }

    $escapedInput = [Security.SecurityElement]::Escape($InputRoot)
    $escapedOutput = [Security.SecurityElement]::Escape($ProtectedRoot)
    $confuserProject = @"
<?xml version="1.0" encoding="utf-8"?>
<project outputDir="$escapedOutput" baseDir="$escapedInput" seed="PennyPet-$PennyProductVersion-NINII-1111">
  <rule pattern="true" preset="none" inherit="false">
    <protection id="rename" />
    <protection id="constants" />
    <protection id="ctrl flow" />
    <protection id="anti ildasm" />
  </rule>
  <module path="$ProtectedFileName" />
</project>
"@
    [IO.File]::WriteAllText($ProjectFile, $confuserProject,
        (New-Object Text.UTF8Encoding($false)))

    $confuserLog = Join-Path $BuildRoot "confuser.log"
    & $ConfuserCli -n $ProjectFile *>&1 | Tee-Object -FilePath $confuserLog
    if (-not (Test-Path -LiteralPath $ProtectedExe -PathType Leaf)) {
        throw "ConfuserEx did not create the protected executable."
    }

    $version = [Diagnostics.FileVersionInfo]::GetVersionInfo($ProtectedExe)
    if ($version.FileVersion -ne $PennyAssemblyVersion -or
        $version.ProductVersion -ne $PennyAssemblyVersion) {
        throw "Protected executable version does not match " +
            "ProductVersion.props ($PennyAssemblyVersion)."
    }

    & $ProtectedExe ("--self-test=" + $SelfTestFile)
    Wait-ForFile $SelfTestFile 300
    $selfTest = Get-Content -LiteralPath $SelfTestFile -Raw | ConvertFrom-Json
    if (-not $selfTest.ok) {
        throw "Protected executable self-test failed."
    }
    & $ProtectedExe ("--startup-probe=" + $StartupProbeFile)
    Wait-ForFile $StartupProbeFile 60
    $startup = Get-Content -LiteralPath $StartupProbeFile -Raw |
        ConvertFrom-Json
    if (-not $startup.ok -or -not $startup.startup_cache_used) {
        throw "Protected executable startup probe failed."
    }

    $binaryText = [Text.Encoding]::Unicode.GetString(
        [IO.File]::ReadAllBytes($ProtectedExe))
    foreach ($clue in @(
        "A_JOINT_ARTWORK_BY_NINII_AND_CODEX",
        "1111_IS_AN_ANGEL_NUMBER",
        "PENNY_TAI_FIVE_GOLDEN_MELODY_ONE_GOLDEN_HORSE",
        "BUDDHA_JUMPS_OVER_THE_WALL_IS_HER_BAND",
        "ISAAC")) {
        if ($binaryText.IndexOf($clue, [StringComparison]::Ordinal) -lt 0) {
            throw "Protected executable is missing easter egg: $clue"
        }
    }

    $outputParent = Split-Path -Parent $FinalOutput
    if (-not [String]::IsNullOrWhiteSpace($outputParent)) {
        New-Item -ItemType Directory -Force -Path $outputParent | Out-Null
    }
    Copy-Item -LiteralPath $ProtectedExe -Destination $FinalOutput -Force
    $hash = (Get-FileHash -Algorithm SHA256 -LiteralPath $FinalOutput).Hash
    Write-Host ""
    Write-Host ("Protected Penny " + $PennyProductVersion +
        " built and verified:") -ForegroundColor Green
    Write-Host $FinalOutput
    Write-Host ("Size: " + [Math]::Round(
        (Get-Item -LiteralPath $FinalOutput).Length / 1MB, 2) + " MiB")
    Write-Host ("Startup probe: " + $startup.elapsed_milliseconds + " ms")
    Write-Host ("SHA-256: " + $hash)
}
finally {
    $resolvedBuildRoot = [IO.Path]::GetFullPath($BuildRoot)
    if ($resolvedBuildRoot.StartsWith($TemporaryParent + '\',
        [StringComparison]::OrdinalIgnoreCase) -and
        (Test-Path -LiteralPath $resolvedBuildRoot)) {
        Remove-Item -LiteralPath $resolvedBuildRoot -Recurse -Force
    }
}
