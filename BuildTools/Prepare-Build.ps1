Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
try {
    $root = Split-Path -Parent $PSScriptRoot
    if (-not (Test-Path -LiteralPath (Join-Path $root 'SX3 SCANER.csproj'))) { throw 'Wrong project.' }
    $expected = 'https://github.com/hieuvipro94x/SX3-SCANNER-MAIN-NEW-V2.git'
    $url = & git -C $root remote get-url origin
    if ($LASTEXITCODE -ne 0 -or $url -ne $expected) { throw "Unexpected origin: $url" }
    & powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'Sync-BuildSource.ps1') -RepositoryPath $root -Remote origin -ExpectedRepositoryUrl $expected
    exit $LASTEXITCODE
} catch {
    Write-Host "SOURCE CHECK FAILED: $_" -ForegroundColor Red
    exit 1
}
