param([switch]$NoPause, [string]$LanRoot = '\\192.168.10.150\public\NAM\SX3Scanner')
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$expected = 'https://github.com/hieuvipro94x/SX3-SCANNER-MAIN-NEW-V2.git'
function Git-Checked([string[]]$Arguments) {
    & git -C $root @Arguments
    if ($LASTEXITCODE -ne 0) { throw "Git failed: $($Arguments -join ' ')" }
}
function Confirm-Step([string]$Prompt) {
    return (Read-Host "$Prompt [Y/N]") -match '^(?i:y|yes)$'
}
try {
    $branch = Git-Checked @('symbolic-ref', '--quiet', '--short', 'HEAD')
    $url = Git-Checked @('remote', 'get-url', 'origin')
    if ($url -ne $expected) { throw "Unexpected origin: $url" }
    Write-Host "Repository: $expected`nBranch: $branch`nLAN: $LanRoot" -ForegroundColor Cyan
    $nextVersion = & (Join-Path $PSScriptRoot 'Increment-Version.ps1') -Preview
    if (-not (Confirm-Step "BUILD V$nextVersion Release x64, run tests and copy to LAN?")) { exit 0 }
    & powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'Build-Release.ps1') -NoPause
    if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
    $resultPath = Join-Path $root 'artifacts\last-build.json'
    $result = Get-Content -LiteralPath $resultPath -Raw | ConvertFrom-Json
    $output = [IO.Path]::GetFullPath($result.Output)
    if ($result.Version -notmatch '^\d+\.\d+\.\d+$') { throw 'Invalid build version.' }
    $expectedOutput = [IO.Path]::GetFullPath((Join-Path $root ("PublishSingle\V" + $result.Version)))
    if (-not $output.Equals($expectedOutput, [StringComparison]::OrdinalIgnoreCase)) { throw 'Invalid version output path.' }

    # Re-fetch after build: never publish source that became stale while building.
    & powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'Sync-BuildSource.ps1') -RepositoryPath $root -Remote origin -ExpectedRepositoryUrl $expected -VerifyCurrent
    if ($LASTEXITCODE -ne 0) { throw 'Source changed remotely. Run BUILD_FILE.cmd again.' }

    $lanParent = Split-Path -Parent $LanRoot
    & powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'Test-LanReleasePath.ps1') -Path $lanParent -TimeoutSeconds 5
    if ($LASTEXITCODE -ne 0) { throw "LAN unavailable. Local build retained at $output." }
    New-Item -ItemType Directory -Path $LanRoot -Force | Out-Null
    $destination = Join-Path $LanRoot ("V" + $result.Version)
    & powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'Copy-VerifiedRelease.ps1') -Source $output -Destination $destination
    if ($LASTEXITCODE -ne 0) { throw 'LAN copy or SHA256 verification failed.' }
    Write-Host "LAN copy verified: $destination" -ForegroundColor Green

    Git-Checked @('status', '--short')
    if (-not (Confirm-Step "COMMIT all source changes and PUSH branch $branch to $expected ?")) { exit 0 }
    Git-Checked @('add', '-A')
    & git -C $root diff --cached --quiet
    $diffCode = $LASTEXITCODE
    if ($diffCode -eq 1) { Git-Checked @('commit', '-m', "Release SX3 Scanner V$($result.Version)") }
    elseif ($diffCode -ne 0) { throw 'Cannot inspect staged changes.' }
    Git-Checked @('log', '--oneline', '-5')
    if (-not (Confirm-Step "Confirm PUSH HEAD to origin/$branch now?")) { exit 0 }
    # Re-check destination immediately before push. Git rejects non-fast-forward pushes.
    $url = Git-Checked @('remote', 'get-url', 'origin')
    if ($url -ne $expected) { throw 'Origin changed during build.' }
    Git-Checked @('push', '-u', 'origin', "HEAD:refs/heads/$branch")
    Write-Host "Completed: build, tests, LAN copy, commit and push." -ForegroundColor Green
    exit 0
} catch {
    Write-Host "RELEASE FAILED: $_" -ForegroundColor Red
    exit 1
}
