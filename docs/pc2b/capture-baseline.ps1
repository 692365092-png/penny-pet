param([Parameter(Mandatory=$true)][string]$ReportDirectory)
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path

# Initial baseline capture only. This script intentionally does not support
# checkpoint comparison. Use frozen artifacts for all later PC-2B checkpoints.
$expectedHead = 'c34e057f7c8ef0c7133e1be7409be5952cd4bfbb'
$headOutput = @(& git -C $repo rev-parse HEAD)
if ($LASTEXITCODE -ne 0 -or $headOutput.Count -ne 1) {
    throw 'Cannot verify the baseline checkout HEAD.'
}
$currentHead = $headOutput[0].Trim()
if ($currentHead -ne $expectedHead) {
    throw ('PC-2B baseline is frozen at ' + $expectedHead +
        '; current HEAD is ' + $currentHead +
        '. Do not regenerate baseline artifacts.')
}
$status = @(& git -C $repo status --porcelain --untracked-files=all)
if ($LASTEXITCODE -ne 0) {
    throw 'Cannot verify the baseline checkout status.'
}
if ($status.Count -ne 0) {
    throw 'Baseline capture requires the exact clean baseline checkout.'
}

$prior = @{}
Import-Csv (Join-Path $repo 'docs\pc1\test-classification.csv') | ForEach-Object { $prior[$_.Test] = $_.Classification }

# Mask comments and literals without changing offsets, then balance actual code braces.
function Mask-CSharp([string]$text) {
    [regex]::Replace($text, '(?s)//[^\r\n]*|/\*.*?\*/|@"(?:""|[^"])*"|"(?:\\.|[^"\\])*"|''(?:\\.|[^''\\])*''',
        [System.Text.RegularExpressions.MatchEvaluator]{param($m) (' ' * $m.Length)})
}
function Get-Body([string]$text, [string]$masked, [int]$offset) {
    $open=$masked.IndexOf('{',$offset); $depth=0
    for($i=$open;$i -lt $masked.Length;$i++) {
        if($masked[$i] -eq '{'){$depth++}
        if($masked[$i] -eq '}'){$depth--;if($depth -eq 0){return $text.Substring($open,$i-$open+1)}}
    }
    throw 'Unclosed method'
}
$rows = @()
foreach($file in Get-ChildItem (Join-Path $repo 'desktop-pet\Tests') -Filter '*.cs' | Sort-Object Name) {
    $text=[IO.File]::ReadAllText($file.FullName); $masked=Mask-CSharp $text
    $class=[regex]::Match($text,'public sealed class (\w+)').Groups[1].Value
    $helpers=@{}
    foreach($h in [regex]::Matches($masked,'(?m)^\s*(?:private|internal)\s+(?:static\s+)?[\w<>\[\],]+\s+(\w+)\s*\(')) {
        $helpers[$h.Groups[1].Value]=Get-Body $text $masked $h.Index
    }
    foreach($m in [regex]::Matches($text,'(?s)\[TestMethod\](?:(?!\[TestMethod\]).)*?public void (\w+)\(')) {
        $name=$m.Groups[1].Value
        $body=Get-Body $text $masked ($m.Index+$m.Length)
        $expanded=$body; $seen=@{}
        do {
            $changed=$false
            foreach($key in @($helpers.Keys)) {
                if(-not $seen.ContainsKey($key) -and (Mask-CSharp $expanded) -match ('\b'+[regex]::Escape($key)+'\s*\(')) {
                    $seen[$key]=$true; $expanded+="`n"+$helpers[$key]; $changed=$true
                }
            }
        } while($changed)
        $code=Mask-CSharp $expanded
        $source=$code -match '\b(ReadSource|ReadSourceFile|FindCoreDirectory)\s*\('
        $kind=if($prior.ContainsKey($name)) { $prior[$name].Replace('SOURCE-STRING GUARD','SOURCE_STRING_GUARD').Replace(' ','_') } else { 'PURE_BEHAVIOR' }
        if($source){$kind='SOURCE_STRING_GUARD'}
        if($code -match '\b(DockDividerFollowerMailbox|StickyPlacementRuntime)\b'){$kind='PROTOCOL_BEHAVIOR'}
        $subject=if($kind -eq 'SOURCE_STRING_GUARD'){$expanded}else{$code}
        $domain=switch -Regex ($subject) {
            'StartupLoading|StartupPetPlacement|ResolveStartupPetPlacement' {'Startup';break}
            'SideTab' {'SideTabs';break}
            'Dock|Divider' {'StickyDock';break}
            'StickyNoteCodec|StickyImport|StickyPlacementRules|PetSettingsCodec|Migrate|Fixture' {'PersistenceMigration';break}
            'StickyHostedRuntime|StickyUiCommand|StickyUiEvent|StickyUiThreadHost|StickyWindowSession' {'StickyHostProtocol';break}
            'StickyPlacementRuntime|WindowFactsVersion|DisplayTopology|DisplayGeometry|PetPlacement|NativeDisplay|WindowsDisplay' {'DisplayPlacement';break}
            'Keyboard|Ime|IME|InputMethod|InputAnimation|KeyOverlay' {'Input';break}
            'Weather|Daily|Almanac|Zodiac|SolarTerm|Persona' {'DailyWeather';break}
            'Reminder|Schedule' {'RemindersSchedule';break}
            'Art|Animation|Bitmap' {'ArtAnimation';break}
            'Sticky' {'StickyEditorPersistence';break}
            default {'CoreInfrastructure'}
        }
        $category=if($m.Value -match 'ArchitectureSourceBoundary' -or $text.Substring(0,$text.IndexOf('public sealed class')) -match 'ArchitectureSourceBoundary'){'ArchitectureSourceBoundary'}else{''}
        $data=@([regex]::Matches($m.Value,'\[DataRow\((.*?)\)\]'))
        $cases=if($data.Count){$data.Count}else{1}
        $calls=@([regex]::Matches($code,'\b([A-Z]\w*)\s*(?:\.|\()') | ForEach-Object {$_.Groups[1].Value} | Where-Object {$_ -notin @('Assert','CollectionAssert','StringAssert','String','Math','List','Dictionary','HashSet','DateTime','TimeSpan','IntPtr','Action','Func','Array','Convert','Regex')} | Sort-Object -Unique)
        for($case=0;$case -lt $cases;$case++) {
            $rows += [pscustomobject][ordered]@{
                file=$file.FullName.Substring($repo.Length+1).Replace('\','/'); class=$class; method=$name
                category=$category; evidence_kind=$kind; domain=$domain
                uses_source_reader=($kind -eq 'SOURCE_STRING_GUARD'); executes_production_logic=($kind -ne 'SOURCE_STRING_GUARD')
                touches_native_windows=$false
                fixture_dependency=if($expanded -match '(?i)fixture|Tests[/\\]Fixtures'){'fixture/helper references in method closure'}else{''}
                notes=('calls='+($calls -join '|')+'; case='+($case+1)+'/'+$cases+$(if($data.Count){'; DataRow='+$data[$case].Groups[1].Value}else{''}))
            }
        }
    }
}
if($rows.Count -ne 350){throw "Expected 350 cases, found $($rows.Count)"}
$rows | Export-Csv (Join-Path $PSScriptRoot 'test-classification-current.csv') -NoTypeInformation -Encoding utf8

function Get-OkFields($value,[string]$prefix='') {
    foreach($property in $value.PSObject.Properties) {
        $path=if($prefix){$prefix+'.'+$property.Name}else{$property.Name}
        if($property.Name.EndsWith('_ok')){[pscustomobject]@{path=$path;value=$property.Value}}
        if($property.Value -is [pscustomobject]){Get-OkFields $property.Value $path}
    }
}
$mod=Get-Content (Join-Path $ReportDirectory 'modular.json') -Raw | ConvertFrom-Json
$single=Get-Content (Join-Path $ReportDirectory 'single-file.json') -Raw | ConvertFrom-Json
$modKeys=@(Get-OkFields $mod | Sort-Object path)
$singleKeys=@(Get-OkFields $single | Sort-Object path)
if(-not $mod.ok -or -not $single.ok){throw 'Root ok failed'}
if(@($modKeys | Where-Object {$_.value -ne $true}).Count -or @($singleKeys | Where-Object {$_.value -ne $true}).Count){throw 'False _ok field'}
if(Compare-Object $modKeys $singleKeys -Property path,value){throw 'Report parity mismatch'}
$modKeys.path | Set-Content (Join-Path $PSScriptRoot 'baseline-selftest-ok-keys.txt') -Encoding utf8
Write-Output "cases=$($rows.Count); methods=$(@($rows | Select-Object class,method -Unique).Count); ok_keys=$($modKeys.Count); roots=true; false_fields=0; parity=PASS"
$rows | Group-Object evidence_kind | Select-Object Name,Count | Format-Table
