param(
    [ValidateSet("anycpu", "x86", "x64")]
    [string]$TargetPlatform = "anycpu",
    [string]$OutputFile = ""
)

$ErrorActionPreference = "Stop"

$ProjectRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$ArtSourceRoot = [IO.Path]::GetFullPath((Join-Path $ProjectRoot "..\art"))
$ArtManifestPath = Join-Path $ArtSourceRoot "pet-art.json"
$ContactAuthorImageName = ([char]0x672A).ToString() +
    ([char]0x6807).ToString() + ([char]0x9898).ToString() + "-1.png"
$ContactAuthorImagePath = Join-Path $ArtSourceRoot $ContactAuthorImageName
$TabIconReferencePath = Join-Path $ArtSourceRoot "svg.png"
$LoadingSourcePath = Join-Path $ArtSourceRoot "loading.png"
$IconPath = Join-Path $ProjectRoot "assets\app.ico"
$ManifestPath = Join-Path $ProjectRoot "app.manifest"
$UIAutomationClient = "C:\Windows\Microsoft.NET\assembly\GAC_MSIL\UIAutomationClient\v4.0_4.0.0.0__31bf3856ad364e35\UIAutomationClient.dll"
$UIAutomationTypes = "C:\Windows\Microsoft.NET\assembly\GAC_MSIL\UIAutomationTypes\v4.0_4.0.0.0__31bf3856ad364e35\UIAutomationTypes.dll"
$WindowsBase = "C:\Windows\Microsoft.NET\assembly\GAC_MSIL\WindowsBase\v4.0_4.0.0.0__31bf3856ad364e35\WindowsBase.dll"
$PresentationFramework = "C:\Windows\Microsoft.NET\assembly\GAC_MSIL\PresentationFramework\v4.0_4.0.0.0__31bf3856ad364e35\PresentationFramework.dll"
$WindowsFormsIntegration = "C:\Windows\Microsoft.NET\assembly\GAC_MSIL\WindowsFormsIntegration\v4.0_4.0.0.0__31bf3856ad364e35\WindowsFormsIntegration.dll"
$SystemXaml = "C:\Windows\Microsoft.NET\assembly\GAC_MSIL\System.Xaml\v4.0_4.0.0.0__b77a5c561934e089\System.Xaml.dll"
$PresentationCoreArchitecture = if ($TargetPlatform -eq "x86") { "GAC_32" } else { "GAC_64" }
$PresentationCore = Join-Path "C:\Windows\Microsoft.NET\assembly\$PresentationCoreArchitecture\PresentationCore" "v4.0_4.0.0.0__31bf3856ad364e35\PresentationCore.dll"
$DistPath = Join-Path $ProjectRoot "dist"
$BuildPath = Join-Path ([IO.Path]::GetTempPath()) "penny-pet-build-temp"
$OutputFileName = "Penny pet.exe"
$OutputPath = if ([String]::IsNullOrWhiteSpace($OutputFile)) {
    Join-Path $DistPath $OutputFileName
} else {
    [IO.Path]::GetFullPath($OutputFile)
}
$BuildOutputPath = Join-Path $BuildPath $OutputFileName
$ReleasePackPath = Join-Path $BuildPath "release-art.ppap"
$StartupCachePath = Join-Path $BuildPath "startup-art.cache"
$LoadingFramePath = Join-Path $BuildPath "startup-loading.png"
$ContactFramePath = Join-Path $BuildPath "contact-xiaohongshu.png"
$Sources = Get-ChildItem -LiteralPath $ProjectRoot -Filter "*.cs" |
    ForEach-Object { $_.FullName }

$CompilerCandidates = @(
    "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe",
    "C:\Windows\Microsoft.NET\Framework\v4.0.30319\csc.exe"
)
$Compiler = $CompilerCandidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
$WebExtensionsCandidates = @(
    "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\System.Web.Extensions.dll",
    "C:\Windows\Microsoft.NET\Framework\v4.0.30319\System.Web.Extensions.dll"
)
$WebExtensions = $WebExtensionsCandidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1

if (-not $Compiler) { throw "Windows C# compiler was not found." }
if (-not (Test-Path -LiteralPath $ArtManifestPath)) { throw "Art manifest is missing: $ArtManifestPath" }
if (-not (Test-Path -LiteralPath $ContactAuthorImagePath -PathType Leaf)) {
    throw "Contact author artwork is missing: $ContactAuthorImagePath"
}
if (-not (Test-Path -LiteralPath $TabIconReferencePath -PathType Leaf)) {
    throw "Sticky tab icon reference is missing: $TabIconReferencePath"
}
if (-not (Test-Path -LiteralPath $LoadingSourcePath -PathType Leaf)) {
    throw "Startup loading artwork is missing: $LoadingSourcePath"
}
if (-not (Test-Path -LiteralPath $IconPath)) { throw "Application icon is missing: $IconPath" }
if (-not (Test-Path -LiteralPath $UIAutomationClient)) { throw "UI Automation client library is missing." }
if (-not (Test-Path -LiteralPath $UIAutomationTypes)) { throw "UI Automation types library is missing." }
foreach ($WpfAssembly in @($WindowsBase, $PresentationCore,
    $PresentationFramework, $WindowsFormsIntegration, $SystemXaml)) {
    if (-not (Test-Path -LiteralPath $WpfAssembly)) {
        throw "WPF framework assembly is missing: $WpfAssembly"
    }
}
if (-not $WebExtensions) { throw "System.Web.Extensions.dll was not found." }
if (-not $Sources) { throw "No C# source files were found." }

$ArtManifest = Get-Content -LiteralPath $ArtManifestPath -Raw -Encoding UTF8 |
    ConvertFrom-Json
if ($ArtManifest.schemaVersion -ne 1) { throw "pet-art.json schemaVersion must be 1." }
if (-not $ArtManifest.states) { throw "pet-art.json has no states." }

$ReferencedArt = @{}
foreach ($Property in $ArtManifest.states.PSObject.Properties) {
    $Definition = $Property.Value
    $StatePaths = @()
    if ($null -ne $Definition.file) { $StatePaths += [string]$Definition.file }
    if ($null -ne $Definition.folder) { $StatePaths += [string]$Definition.folder }
    foreach ($RelativePath in $StatePaths) {
        if ([String]::IsNullOrWhiteSpace($RelativePath)) { continue }
        if ([IO.Path]::IsPathRooted($RelativePath)) { throw "Art paths must be relative: $RelativePath" }
        $ReferenceKey = ([String]$RelativePath).ToLowerInvariant()
        if ($ReferencedArt.ContainsKey($ReferenceKey)) { continue }
        $Source = [IO.Path]::GetFullPath((Join-Path $ArtSourceRoot $RelativePath))
        $ArtPrefix = $ArtSourceRoot.TrimEnd('\') + '\'
        if (-not $Source.StartsWith($ArtPrefix, [StringComparison]::OrdinalIgnoreCase)) {
            throw "Art path escapes the art folder: $RelativePath"
        }
        if (-not (Test-Path -LiteralPath $Source -PathType Leaf)) {
            throw "Release embedding currently requires file assets: $RelativePath"
        }
        $ReferencedArt[$ReferenceKey] = [string]$RelativePath
    }
}

New-Item -ItemType Directory -Force -Path $DistPath | Out-Null
New-Item -ItemType Directory -Force -Path $BuildPath | Out-Null
$OutputParent = Split-Path -Parent $OutputPath
if (-not [String]::IsNullOrWhiteSpace($OutputParent)) {
    New-Item -ItemType Directory -Force -Path $OutputParent | Out-Null
}

# Keep the loading resource tiny and pixel-aligned with the first idle frame.
# Preserve the source character's aspect ratio while keeping its center and
# baseline aligned to the established 192x208 pet canvas.
Add-Type -AssemblyName System.Drawing
$LoadingSource = [System.Drawing.Bitmap]::FromFile($LoadingSourcePath)
try {
    $LoadingCanvas = New-Object System.Drawing.Bitmap 192, 208,
        ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    try {
        $LoadingGraphics = [System.Drawing.Graphics]::FromImage($LoadingCanvas)
        try {
            $LoadingGraphics.Clear([System.Drawing.Color]::Transparent)
            $LoadingGraphics.CompositingMode =
                [System.Drawing.Drawing2D.CompositingMode]::SourceCopy
            $LoadingGraphics.InterpolationMode =
                [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
            $LoadingGraphics.PixelOffsetMode =
                [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
            $SourceBounds = New-Object System.Drawing.Rectangle 24, 8, 501, 845
            # Match the idle frame's shoe center (about x=107) and sole
            # baseline (y=205) on the 192x208 canvas.
            $TargetBounds = New-Object System.Drawing.Rectangle 42, 1, 122, 205
            $LoadingGraphics.DrawImage($LoadingSource, $TargetBounds,
                $SourceBounds, [System.Drawing.GraphicsUnit]::Pixel)
        }
        finally { $LoadingGraphics.Dispose() }
        $LoadingCanvas.Save($LoadingFramePath,
            [System.Drawing.Imaging.ImageFormat]::Png)
    }
    finally { $LoadingCanvas.Dispose() }
}
finally { $LoadingSource.Dispose() }

# Embed only the centered Xiaohongshu artwork used by the contact window.
# The removed QQ panel is not carried inside new builds.
$ContactSource = [System.Drawing.Bitmap]::FromFile($ContactAuthorImagePath)
try {
    $ContactCrop = $ContactSource.Clone(
        (New-Object System.Drawing.Rectangle 200, 81, 104, 53),
        [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    try {
        $ContactCrop.Save($ContactFramePath,
            [System.Drawing.Imaging.ImageFormat]::Png)
    }
    finally { $ContactCrop.Dispose() }
}
finally { $ContactSource.Dispose() }

$EmbeddedResourceArgs = @(
    "/resource:$ArtManifestPath,PennyPet.Art.Manifest",
    "/resource:$ContactFramePath,PennyPet.ContactAuthor.Image",
    "/resource:$TabIconReferencePath,PennyPet.TabIcons.Reference",
    "/resource:$LoadingFramePath,PennyPet.Startup.Loading"
)
$CompilerArgs = @(
    "/nologo",
    "/target:winexe",
    "/optimize+",
    "/debug-",
    ("/platform:" + $TargetPlatform),
    "/codepage:65001",
    "/win32manifest:$ManifestPath",
    "/win32icon:$IconPath",
    "/reference:System.dll",
    "/reference:System.Drawing.dll",
    "/reference:System.Windows.Forms.dll",
    "/reference:$WindowsBase",
    "/reference:$PresentationCore",
    "/reference:$PresentationFramework",
    "/reference:$WindowsFormsIntegration",
    "/reference:$SystemXaml",
    "/reference:$WebExtensions",
    "/reference:$UIAutomationClient",
    "/reference:$UIAutomationTypes",
    "/out:$BuildOutputPath"
) + $EmbeddedResourceArgs + $Sources

& $Compiler $CompilerArgs
if ($LASTEXITCODE -ne 0) { throw "Compilation failed with exit code $LASTEXITCODE" }
$PackProcess = Start-Process -FilePath $BuildOutputPath -ArgumentList @(
    ('--write-release-pack="' + $ReleasePackPath + '"')
) -WorkingDirectory (Split-Path -Parent $ArtSourceRoot) -PassThru -Wait -WindowStyle Hidden
if ($PackProcess.ExitCode -ne 0 -or
    -not (Test-Path -LiteralPath $ReleasePackPath -PathType Leaf)) {
    throw "Release art pack generation failed."
}
$CacheProcess = Start-Process -FilePath $BuildOutputPath -ArgumentList @(
    ('--write-startup-cache="' + $StartupCachePath + '"')
) -WorkingDirectory (Split-Path -Parent $ArtSourceRoot) -PassThru -Wait -WindowStyle Hidden
if ($CacheProcess.ExitCode -ne 0 -or
    -not (Test-Path -LiteralPath $StartupCachePath -PathType Leaf)) {
    throw "Startup art cache generation failed."
}
$FinalCompilerArgs = $CompilerArgs +
    "/resource:$ReleasePackPath,PennyPet.Art.ReleasePack" +
    "/resource:$StartupCachePath,PennyPet.Art.StartupCache"
& $Compiler $FinalCompilerArgs
if ($LASTEXITCODE -ne 0) {
    throw "Final compilation failed with exit code $LASTEXITCODE"
}
Copy-Item -LiteralPath $BuildOutputPath -Destination $OutputPath -Force
Remove-Item -LiteralPath $ReleasePackPath -Force
Remove-Item -LiteralPath $StartupCachePath -Force
Remove-Item -LiteralPath $LoadingFramePath -Force
Remove-Item -LiteralPath $ContactFramePath -Force
Remove-Item -LiteralPath $BuildOutputPath -Force
Remove-Item -LiteralPath $BuildPath -Force

Write-Host ""
Write-Host ($OutputFileName + " was built successfully:") -ForegroundColor Green
Write-Host $OutputPath
Write-Host ("Target platform: " + $TargetPlatform)
Write-Host "Art bundle: embedded in the EXE"
Write-Host "Animation source: full-resolution lossless release pack (GIF-free runtime)"
Write-Host "Startup idle frames: compressed predecoded cache"
