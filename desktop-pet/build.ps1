param(
    [ValidateSet("anycpu", "x86", "x64")]
    [string]$TargetPlatform = "anycpu",
    [string]$OutputFile = ""
)

$ErrorActionPreference = "Stop"

$ProjectRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$ProjectFile = Join-Path $ProjectRoot "PennyPet.Windows.csproj"
$BuiltOutputDir = Join-Path $ProjectRoot "bin"
if ($TargetPlatform -ne "anycpu") {
    $BuiltOutputDir = Join-Path $BuiltOutputDir $TargetPlatform
}
$BuiltOutputDir = Join-Path $BuiltOutputDir "Release\net48"
$BuiltExe = Join-Path $BuiltOutputDir "Penny pet.exe"
$DefaultOutput = Join-Path $ProjectRoot "dist\Penny pet.exe"
$OutputPath = if ([String]::IsNullOrWhiteSpace($OutputFile)) {
    $DefaultOutput
} else {
    [IO.Path]::GetFullPath($OutputFile)
}

$BuildArguments = @("build", $ProjectFile, "/p:Configuration=Release")
if ($TargetPlatform -ne "anycpu") {
    $BuildArguments += "/p:Platform=$TargetPlatform"
    $BuildArguments += "/p:PlatformTarget=$TargetPlatform"
}

dotnet @BuildArguments
if ($LASTEXITCODE -ne 0) {
    throw "dotnet build failed with exit code $LASTEXITCODE"
}

$OutputParent = Split-Path -Parent $OutputPath
if (-not [String]::IsNullOrWhiteSpace($OutputParent)) {
    New-Item -ItemType Directory -Force -Path $OutputParent | Out-Null
}
Copy-Item -LiteralPath $BuiltExe -Destination $OutputPath -Force

Write-Host ""
Write-Host ("Penny pet.exe was built successfully:") -ForegroundColor Green
Write-Host $OutputPath
Write-Host ("Target platform: " + $TargetPlatform)
