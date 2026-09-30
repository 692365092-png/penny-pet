#requires -Version 5.1
param([Parameter(Mandatory = $true)][string]$Executable)

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Web.Extensions
$assembly = [Reflection.Assembly]::LoadFrom((Resolve-Path -LiteralPath $Executable).Path)
$types = $assembly.GetTypes()
$serializer = New-Object System.Web.Script.Serialization.JavaScriptSerializer

# Exercise the actual release DTOs after renaming. Ordinary self-tests use
# the unprotected assembly and cannot detect broken JSON property names.
function Read-WeatherResponse([string]$TypeName, [string]$Json) {
    $matches = @($types | Where-Object { $_.Name -eq $TypeName })
    if ($matches.Count -ne 1) { throw "Missing preserved weather JSON type: $TypeName" }
    return $serializer.Deserialize($Json, $matches[0])
}

$city = Read-WeatherResponse "GeocodingResponse" @'
{"results":[{"name":"武汉","admin1":"湖北","country":"中国","latitude":30.58333,"longitude":114.26667,"timezone":"Asia/Shanghai"}]}
'@
if ($city.results.Count -ne 1 -or $city.results[0].name -ne "武汉" -or
    $city.results[0].admin1 -ne "湖北" -or $city.results[0].country -ne "中国" -or
    $city.results[0].latitude -ne 30.58333 -or $city.results[0].longitude -ne 114.26667 -or
    $city.results[0].timezone -ne "Asia/Shanghai") {
    throw "Release cannot deserialize city search results."
}
$empty = Read-WeatherResponse "GeocodingResponse" '{"generationtime_ms":0.1}'
if ($null -ne $empty.results) { throw "Empty city search returned unexpected results." }

$forecast = Read-WeatherResponse "ForecastResponse" @'
{"utc_offset_seconds":28800,"hourly":{"time":["2026-09-30T12:00"],"temperature_2m":[25],"apparent_temperature":[27],"precipitation_probability":[80],"precipitation":[1.2],"snowfall":[0],"weather_code":[61],"wind_speed_10m":[12],"wind_gusts_10m":[20]}}
'@
if ($forecast.utc_offset_seconds -ne 28800 -or
    $forecast.hourly.time[0] -ne "2026-09-30T12:00" -or
    $forecast.hourly.temperature_2m[0] -ne 25 -or
    $forecast.hourly.apparent_temperature[0] -ne 27 -or
    $forecast.hourly.precipitation_probability[0] -ne 80 -or
    $forecast.hourly.precipitation[0] -ne 1.2 -or
    $forecast.hourly.snowfall[0] -ne 0 -or
    $forecast.hourly.weather_code[0] -ne 61 -or
    $forecast.hourly.wind_speed_10m[0] -ne 12 -or
    $forecast.hourly.wind_gusts_10m[0] -ne 20) {
    throw "Release cannot deserialize weather forecast results."
}
Write-Host "Release city search and weather JSON contracts passed."
