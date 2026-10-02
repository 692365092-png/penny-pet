param([Parameter(Mandatory=$true)][string]$ReportPath)
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '../../..')).Path
$project = Join-Path $PSScriptRoot 'ArtPackageBenchmark.csproj'
$work = Join-Path ([IO.Path]::GetTempPath()) ('penny-art-experiment-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $work | Out-Null
$report = [ordered]@{
    ok = $false
    observational = $true
    environment = 'Windows, fresh process per sample; OS file cache is not flushed'
    scope = '192x208 embedded idle-ready boundary, not complete application first paint'
    samples = @()
}
function Run-Probe([string]$variant, [string]$mode, [string]$output) {
    $exe = Join-Path $work ($variant + '/PennyPet.ArtPackageBenchmark.exe')
    # No art/ folder in this working directory or beside the executable.
    Push-Location $work
    try {
        & $exe $mode $output
        if ($LASTEXITCODE -ne 0) { throw "Art probe failed: $variant / $mode; see $output" }
    } finally { Pop-Location }
    $value = Get-Content -LiteralPath $output -Raw | ConvertFrom-Json
    if (-not $value.ok) { throw "Art probe reported failure: $($value.error)" }
    if ($variant -eq 'dual' -and $value.idleSource -ne 'embedded-startup-cache') {
        throw 'Dual probe did not decode the startup cache.'
    }
    if ($variant -eq 'single' -and $value.idleSource -notlike 'embedded-pack:*') {
        throw 'Single probe did not decode the release pack.'
    }
    $value | Add-Member -NotePropertyName variant -NotePropertyValue $variant
    return $value
}
function Median($values) {
    $sorted = @($values | Sort-Object)
    return $sorted[[int][Math]::Floor($sorted.Count / 2)]
}
try {
    foreach ($variant in @('dual', 'single')) {
        $cache = if ($variant -eq 'dual') { 'true' } else { 'false' }
        dotnet build $project --configuration Release "-p:StartupCache=$cache" `
            ("-p:OutputPath=" + (Join-Path $work "$variant/")) `
            ("-p:IntermediateOutputPath=" + (Join-Path $work "obj-$variant/"))
        if ($LASTEXITCODE -ne 0) { throw "Art probe build failed: $variant" }
    }
    # Alternate launch order to reduce systematic ordering bias. Every measured
    # sample has a fresh CLR/JIT; this is not a claim of cold physical disk IO.
    for ($iteration = 0; $iteration -lt 7; $iteration++) {
        $order = if ($iteration % 2 -eq 0) { @('dual', 'single') } else { @('single', 'dual') }
        foreach ($variant in $order) {
            $value = Run-Probe $variant 'startup' (Join-Path $work "$variant-$iteration.json")
            $value | Add-Member -NotePropertyName iteration -NotePropertyValue $iteration
            $report.samples += $value
        }
    }
    $dual = Run-Probe 'dual' 'verify' (Join-Path $work 'dual-verify.json')
    $single = Run-Probe 'single' 'verify' (Join-Path $work 'single-verify.json')
    if (@($dual.states).Count -ne 10 -or @($single.states).Count -ne 10) {
        throw 'Incomplete state verification.'
    }
    for ($row = 0; $row -lt 10; $row++) {
        if ($dual.states[$row].state -ne $single.states[$row].state -or
            $dual.states[$row].digest -ne $single.states[$row].digest) {
            throw "Pixel/duration mismatch at state $row."
        }
    }
    $report.verification = [ordered]@{ dual = $dual; single = $single; exactPixelsAndDurations = $true }
    $report.summary = @()
    foreach ($variant in @('dual', 'single')) {
        $samples = @($report.samples | Where-Object { $_.variant -eq $variant })
        $report.summary += [ordered]@{
            variant = $variant
            medianIdleReadyMs = Median @($samples.idleReadyMs)
            medianIdleCpuMs = Median @($samples.idleCpuMs)
            medianIdlePrivateBytesDelta = Median @($samples.idlePrivateBytesDelta)
            medianIdlePeakWorkingSetBytes = Median @($samples.idlePeakWorkingSetBytes)
            executableBytes = $samples[0].executableBytes
            resourceBytes = $samples[0].releasePackBytes + $samples[0].startupCacheBytes
        }
    }
    # Verify that rerunning the existing encoders produces the same bytes.
    $tool = Join-Path $repo 'desktop-pet/bin/Release/net48/PennyPet.Tools.exe'
    $generated = Join-Path $repo 'desktop-pet/obj/PennyPet.ArtResources/Release/net48'
    $report.reproducibility = @()
    Push-Location $repo
    try {
        foreach ($entry in @(
            @{ name = 'release-art.ppap'; command = '--write-release-pack=' },
            @{ name = 'startup-art.cache'; command = '--write-startup-cache=' })) {
            $output = Join-Path $work $entry.name
            & $tool ($entry.command + $output)
            if ($LASTEXITCODE -ne 0) { throw "Art regeneration failed: $($entry.name)" }
            $expected = (Get-FileHash (Join-Path $generated $entry.name) -Algorithm SHA256).Hash
            $actual = (Get-FileHash $output -Algorithm SHA256).Hash
            if ($expected -ne $actual) { throw "Art regeneration differs: $($entry.name)" }
            $report.reproducibility += @{ file = $entry.name; sha256 = $actual }
        }
    } finally { Pop-Location }
    $report.ok = $true
} catch {
    $report.error = $_.Exception.ToString()
} finally {
    $json = $report | ConvertTo-Json -Depth 12
    Set-Content -LiteralPath $ReportPath -Value $json -Encoding UTF8
    Write-Host $json
    Remove-Item -LiteralPath $work -Recurse -Force
}
if (-not $report.ok) { throw 'Art package experiment failed; inspect its JSON report.' }
