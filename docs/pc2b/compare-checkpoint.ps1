param(
    [Parameter(Mandatory = $true)]
    [string]$ReportDirectory
)

# Historical artifacts are fixed inputs. This tool only reads, compares and prints.
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$frozen = @(
    @{ Path = 'docs/pc2b/baseline-modular.json'; Blob = '04270ec294092aaede9f02442d3d176dfc827f09' },
    @{ Path = 'docs/pc2b/baseline-single-file.json'; Blob = '04270ec294092aaede9f02442d3d176dfc827f09' },
    @{ Path = 'docs/pc2b/baseline-selftest-ok-keys.txt'; Blob = '55794d1fcaa975c0ce2acfcb50830988fb67dd07' },
    @{ Path = 'docs/pc2b/test-classification-current.csv'; Blob = 'a024d2d2db165db3c54c427b4965e21745230879' }
)
foreach ($item in $frozen) {
    $blob = @(& git -C $repo rev-parse ("HEAD:" + $item.Path))
    if ($LASTEXITCODE -ne 0 -or $blob.Count -ne 1 -or $blob[0].Trim() -cne $item.Blob) {
        throw ('Frozen PC-2B artifact changed in HEAD: ' + $item.Path)
    }
    & git -C $repo diff --quiet -- $item.Path
    if ($LASTEXITCODE -ne 0) { throw ('Frozen artifact modified in working tree: ' + $item.Path) }
    & git -C $repo diff --cached --quiet -- $item.Path
    if ($LASTEXITCODE -ne 0) { throw ('Frozen artifact modified in index: ' + $item.Path) }
}

function Get-OkFields($value, [string]$prefix = '') {
    if ($value -is [array]) {
        for ($i = 0; $i -lt $value.Count; $i++) {
            Get-OkFields $value[$i] ($prefix + '[' + $i + ']')
        }
    } elseif ($value -is [pscustomobject]) {
        foreach ($property in $value.PSObject.Properties) {
            $path = if ($prefix) { $prefix + '.' + $property.Name } else { $property.Name }
            if ($property.Name.EndsWith('_ok', [StringComparison]::Ordinal)) {
                [pscustomobject]@{ path = $path; value = $property.Value }
            }
            Get-OkFields $property.Value $path
        }
    }
}

function Read-Report([string]$path) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw ('Missing report: ' + $path) }
    $report = Get-Content -LiteralPath $path -Raw | ConvertFrom-Json
    if ($report -isnot [pscustomobject] -or $report.ok -isnot [bool] -or -not $report.ok) {
        throw ('SelfTest root ok must be boolean true: ' + $path)
    }
    return $report
}

function New-OkMap($report) {
    $map = [System.Collections.Generic.Dictionary[string,bool]]::new([StringComparer]::Ordinal)
    foreach ($field in @(Get-OkFields $report)) {
        if ($field.value -isnot [bool]) { throw ('Non-boolean _ok field: ' + $field.path) }
        if ($map.ContainsKey($field.path)) { throw ('Duplicate _ok path: ' + $field.path) }
        $map.Add($field.path, $field.value)
    }
    return ,$map
}

function Assert-Parity($left, $right, [string]$label) {
    $differences = @(
        foreach ($key in $left.Keys) {
            if (-not $right.ContainsKey($key)) { 'Only modular: ' + $key }
            elseif ($left[$key] -ne $right[$key]) { 'Different values: ' + $key }
        }
        foreach ($key in $right.Keys) {
            if (-not $left.ContainsKey($key)) { 'Only single-file: ' + $key }
        }
    )
    if ($differences.Count -ne 0) {
        $differences | Sort-Object | ForEach-Object { Write-Host $_ }
        throw ($label + ' modular/single-file _ok parity mismatch.')
    }
}

$baselineMod = New-OkMap (Read-Report (Join-Path $PSScriptRoot 'baseline-modular.json'))
$baselineSingle = New-OkMap (Read-Report (Join-Path $PSScriptRoot 'baseline-single-file.json'))
$baselineKeys = @(Get-Content -LiteralPath (Join-Path $PSScriptRoot 'baseline-selftest-ok-keys.txt') |
    ForEach-Object { $_.Trim() } | Where-Object { $_ -ne '' })
$baselineSet = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
foreach ($key in $baselineKeys) {
    if (-not $baselineSet.Add($key)) { throw ('Duplicate frozen key: ' + $key) }
}
if ($baselineMod.Count -ne 306 -or $baselineSingle.Count -ne 306 -or $baselineKeys.Count -ne 306) {
    throw 'Frozen baseline must contain exactly 306 _ok paths in each artifact.'
}
Assert-Parity $baselineMod $baselineSingle 'Frozen baseline'
foreach ($key in $baselineKeys) {
    if (-not $baselineMod.ContainsKey($key) -or -not $baselineMod[$key]) {
        throw ('Frozen baseline key missing or false: ' + $key)
    }
}

$currentMod = New-OkMap (Read-Report (Join-Path $ReportDirectory 'modular.json'))
$currentSingle = New-OkMap (Read-Report (Join-Path $ReportDirectory 'single-file.json'))
$missing = @()
$falseBaseline = @()
foreach ($key in $baselineKeys) {
    if (-not $currentMod.ContainsKey($key) -or -not $currentSingle.ContainsKey($key)) { $missing += $key }
    elseif (-not $currentMod[$key] -or -not $currentSingle[$key]) { $falseBaseline += $key }
}
if ($missing.Count -ne 0 -or $falseBaseline.Count -ne 0) {
    $missing | ForEach-Object { Write-Host ('Missing baseline key: ' + $_) }
    $falseBaseline | ForEach-Object { Write-Host ('False baseline key: ' + $_) }
    throw ('Baseline gate failed: missing=' + $missing.Count + '; false=' + $falseBaseline.Count)
}
Assert-Parity $currentMod $currentSingle 'Current'
$extra = @($currentMod.Keys | Where-Object { -not $baselineSet.Contains($_) } | Sort-Object)
Write-Output 'PC2B checkpoint comparison PASS'
Write-Output 'baseline_keys=306'
Write-Output 'baseline_missing=0'
Write-Output 'baseline_false=0'
Write-Output ('current_modular_ok_keys=' + $currentMod.Count)
Write-Output ('current_single_file_ok_keys=' + $currentSingle.Count)
Write-Output ('extra_keys=' + $extra.Count)
Write-Output 'root_ok=true'
Write-Output 'parity=PASS'
$extra | ForEach-Object { Write-Output ('extra: ' + $_) }
