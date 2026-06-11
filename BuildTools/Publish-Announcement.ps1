param(
    [Parameter(Mandatory = $true)]
    [string]$ServerUrl,

    [Parameter(Mandatory = $true)]
    [string]$Token,

    [string]$File = (Join-Path $PSScriptRoot "..\announcement.json")
)

$ErrorActionPreference = "Stop"

$resolvedFile = (Resolve-Path -LiteralPath $File).Path
$strictUtf8 = [Text.UTF8Encoding]::new($false, $true)
$utf8WithoutBom = [Text.UTF8Encoding]::new($false)
$jsonBytes = [IO.File]::ReadAllBytes($resolvedFile)
$json = $strictUtf8.GetString($jsonBytes)
$null = $json | ConvertFrom-Json
[IO.File]::WriteAllText($resolvedFile, $json, $utf8WithoutBom)

$endpoint = $ServerUrl.TrimEnd("/") + "/api/announcements"
$headers = @{
    Authorization = "Bearer $Token"
}

$response = Invoke-RestMethod `
    -Method Post `
    -Uri $endpoint `
    -Headers $headers `
    -ContentType "application/json; charset=utf-8" `
    -Body ($utf8WithoutBom.GetBytes($json))

$response | Format-List
