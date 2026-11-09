$ErrorActionPreference = 'Stop'
$tokens = $null
$parseErrors = $null
$source = Join-Path (Split-Path -Parent $PSScriptRoot) 'BuildTools\release-all.ps1'
$ast = [System.Management.Automation.Language.Parser]::ParseFile($source, [ref]$tokens, [ref]$parseErrors)
if ($parseErrors.Count) { throw ($parseErrors | Out-String) }
# Load helpers without executing the release entry point.
$ast.FindAll({ param($node) $node -is [System.Management.Automation.Language.FunctionDefinitionAst] }, $false) |
    ForEach-Object { Invoke-Expression $_.Extent.Text }

$gitExe = (Get-Command git.exe).Source
$testRoot = Join-Path ([IO.Path]::GetTempPath()) ('sx3-build-test-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $testRoot | Out-Null
function Run-Git([string[]]$Arguments) {
    & $gitExe @Arguments | Out-Host
    if ($LASTEXITCODE -ne 0) { throw "Test Git command failed: $Arguments" }
}
function Check($Condition, $Message) { if (-not $Condition) { throw $Message } }
function Save-Commit($Repo, $Message) {
    Run-Git @('-C', $Repo, 'add', '--all')
    Run-Git @('-C', $Repo, 'commit', '-m', $Message)
}
try {
    $remote = Join-Path $testRoot 'remote.git'
    $writer = Join-Path $testRoot 'writer'
    $local = Join-Path $testRoot 'local'
    Run-Git @('init', '--bare', $remote)
    Run-Git @('init', '-b', 'main', $writer)
    Run-Git @('-C', $writer, 'config', 'user.name', 'SX3 Test')
    Run-Git @('-C', $writer, 'config', 'user.email', 'sx3-test@example.invalid')
    Set-Content (Join-Path $writer 'shared.txt') 'base'
    Set-Content (Join-Path $writer 'settings.txt') 'base'
    Save-Commit $writer 'base'
    Run-Git @('-C', $writer, 'remote', 'add', 'origin', $remote)
    Run-Git @('-C', $writer, 'push', '-u', 'origin', 'main')
    Run-Git @('clone', '-b', 'main', $remote, $local)
    Run-Git @('-C', $local, 'config', 'user.name', 'SX3 Test')
    Run-Git @('-C', $local, 'config', 'user.email', 'sx3-test@example.invalid')
    Assert-GitIdentity $local $gitExe

    Set-Content (Join-Path $writer 'remote.txt') 'new remote source'
    Save-Commit $writer 'remote update'
    Run-Git @('-C', $writer, 'push')
    Set-Content (Join-Path $local 'settings.txt') 'staged local edit'
    Run-Git @('-C', $local, 'add', 'settings.txt')
    Set-Content (Join-Path $local 'new.txt') 'untracked local file'
    Sync-SourceBeforeBuild $local $gitExe 'main'
    Check (Test-Path (Join-Path $local 'remote.txt')) 'Remote source not synchronized'
    Check ((Get-Content (Join-Path $local 'new.txt')) -eq 'untracked local file') 'Untracked data lost'
    $staged = & $gitExe -C $local diff --cached --name-only
    Check ($staged -eq 'settings.txt') 'Staged state was not restored'
    Write-Host 'PASS: behind branch synchronizes and preserves staged/untracked edits'
    Save-Commit $local 'local edits'
    Set-Content (Join-Path $writer 'second.txt') 'another remote change'
    Save-Commit $writer 'second remote update'
    Run-Git @('-C', $writer, 'push')
    Sync-SourceBeforeBuild $local $gitExe 'main'
    Run-Git @('-C', $local, 'merge-base', '--is-ancestor', 'origin/main', 'HEAD')
    Check ((Get-Content (Join-Path $local 'settings.txt')) -eq 'staged local edit') 'Local commit lost'
    Write-Host 'PASS: divergent branch rebases and preserves local commit'

    Set-Content (Join-Path $local 'shared.txt') 'local conflicting commit'
    Save-Commit $local 'local conflict'
    Set-Content (Join-Path $writer 'shared.txt') 'remote conflicting commit'
    Save-Commit $writer 'remote conflict'
    Run-Git @('-C', $writer, 'push')
    Set-Content (Join-Path $local 'new.txt') 'unsaved work'
    $before = & $gitExe -C $local rev-parse HEAD
    $failed = $false
    try { Sync-SourceBeforeBuild $local $gitExe 'main' }
    catch {
        Check ($_.Exception.Message -match 'Commit local xung dot') 'Unexpected synchronization failure'
        $failed = $true
    }
    Check $failed 'Expected a rebase conflict'
    Check ((& $gitExe -C $local rev-parse HEAD) -eq $before) 'Failed rebase changed original HEAD'
    Check ((Get-Content (Join-Path $local 'new.txt')) -eq 'unsaved work') 'Unsaved work lost on failed rebase'
    Assert-NoGitOperation $local $gitExe
    Write-Host 'PASS: conflicting rebase aborts and restores unsaved work'

    Run-Git @('-C', $local, 'config', '--unset-all', 'user.name')
    Run-Git @('-C', $local, 'config', '--unset-all', 'user.email')
    Run-Git @('-C', $local, 'config', 'user.useConfigOnly', 'true')
    $savedGlobalConfig = $env:GIT_CONFIG_GLOBAL
    $env:GIT_CONFIG_GLOBAL = Join-Path $testRoot 'empty-global-config'
    $failed = $false
    try {
        try { Assert-GitIdentity $local $gitExe }
        catch { $failed = $_.Exception.Message -match 'Git thieu ten/email' }
    }
    finally { $env:GIT_CONFIG_GLOBAL = $savedGlobalConfig }
    Check $failed 'Missing Git identity was not rejected'
    Write-Host 'PASS: missing identity stops before build'
}
finally {
    $resolved = [IO.Path]::GetFullPath($testRoot)
    $tempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\') + '\'
    if ($resolved.StartsWith($tempRoot, [StringComparison]::OrdinalIgnoreCase) -and
        (Split-Path -Leaf $resolved) -like 'sx3-build-test-*') {
        Remove-Item -LiteralPath $resolved -Recurse -Force
    }
}
