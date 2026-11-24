$ErrorActionPreference = 'Stop'
$sync = Join-Path (Split-Path -Parent $PSScriptRoot) 'BuildTools\Sync-BuildSource.ps1'
$testRoot = Join-Path ([IO.Path]::GetTempPath()) ('sx3-source-sync-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $testRoot | Out-Null
function Run-TestGit([string[]]$Arguments) {
    & git @Arguments | Out-Host
    if ($LASTEXITCODE -ne 0) { throw "Git failed: $Arguments" }
}
function Check($Value, $Message) { if (-not $Value) { throw $Message } }
$remote = Join-Path $testRoot 'remote.git'
$writer = Join-Path $testRoot 'writer'
$local = Join-Path $testRoot 'local'
Run-TestGit @('init', '--bare', $remote)
Run-TestGit @('init', '-b', 'main', $writer)
Run-TestGit @('-C', $writer, 'config', 'user.name', 'SX3 Test')
Run-TestGit @('-C', $writer, 'config', 'user.email', 'sx3@example.invalid')
New-Item -ItemType Directory -Path (Join-Path $writer 'Properties') | Out-Null
Set-Content (Join-Path $writer 'SX3 SCANER.csproj') '<Project />'
Set-Content (Join-Path $writer 'Properties\AssemblyInfo.cs') '[assembly: AssemblyVersion("1.0.0")]'
Set-Content (Join-Path $writer 'local.txt') 'base'
Run-TestGit @('-C', $writer, 'add', '-A')
Run-TestGit @('-C', $writer, 'commit', '-m', 'base')
Run-TestGit @('-C', $writer, 'remote', 'add', 'origin', $remote)
Run-TestGit @('-C', $writer, 'push', '-u', 'origin', 'main')
Run-TestGit @('clone', '-b', 'main', $remote, $local)
Run-TestGit @('-C', $writer, 'switch', '-c', 'release/latest')
Set-Content (Join-Path $writer 'Properties\AssemblyInfo.cs') '[assembly: AssemblyVersion("2.0.0")]'
Set-Content (Join-Path $writer 'remote.txt') 'incoming'
Run-TestGit @('-C', $writer, 'add', '-A')
Run-TestGit @('-C', $writer, 'commit', '-m', 'new release')
Run-TestGit @('-C', $writer, 'push', '-u', 'origin', 'release/latest')
Set-Content (Join-Path $local 'local.txt') 'staged edit'
Run-TestGit @('-C', $local, 'add', 'local.txt')
Set-Content (Join-Path $local 'untracked.txt') 'preserve me'
& powershell.exe -NoProfile -ExecutionPolicy Bypass -File $sync -RepositoryPath $local -Remote origin -ExpectedRepositoryUrl $remote -VerifyCurrent
Check ($LASTEXITCODE -eq 1) 'VerifyCurrent must reject stale source'
Check (-not (Test-Path (Join-Path $local 'remote.txt'))) 'VerifyCurrent changed working tree'
& powershell.exe -NoProfile -ExecutionPolicy Bypass -File $sync -RepositoryPath $local -Remote origin -ExpectedRepositoryUrl $remote
Check ($LASTEXITCODE -eq 10) 'Expected source update and launcher restart'
Check (Test-Path (Join-Path $local 'remote.txt')) 'Newest branch was not synchronized'
Check ((Get-Content (Join-Path $local 'untracked.txt')) -eq 'preserve me') 'Untracked edit lost'
Check ((& git -C $local diff --cached --name-only) -eq 'local.txt') 'Staged state lost'
& powershell.exe -NoProfile -ExecutionPolicy Bypass -File $sync -RepositoryPath $local -Remote origin -ExpectedRepositoryUrl $remote
Check ($LASTEXITCODE -eq 0) 'Up-to-date source must succeed'
Write-Host 'PASS: newest release branch, stale-source guard, staged/untracked preservation and repeat sync'
Write-Host "Test fixtures retained at $testRoot"
