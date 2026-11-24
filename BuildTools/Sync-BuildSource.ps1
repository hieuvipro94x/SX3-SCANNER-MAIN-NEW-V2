param(
    [string]$RepositoryPath = (Split-Path -Parent $PSScriptRoot),
    [string]$Remote = "build-target",
    [string]$ExpectedRepositoryUrl = "https://github.com/hieuvipro94x/SX3-SCANNER-MAIN-NEW-V2.git",
    [switch]$CheckOnly,
    [switch]$VerifyCurrent
)

Set-StrictMode -Version 2.0
$ErrorActionPreference = "Stop"

function Invoke-RepositoryGit([string[]]$Arguments, [switch]$AllowFailure) {
    # Native progress on stderr is informational; the Git exit code is authoritative.
    $ErrorActionPreference = "Continue"
    $lines = @(& git -C $RepositoryPath @Arguments 2>&1 | ForEach-Object { $_.ToString() })
    $code = $LASTEXITCODE
    if ($code -ne 0 -and -not $AllowFailure) {
        throw ("Git failed: git " + ($Arguments -join " ") + "`n" + ($lines -join "`n"))
    }
    [pscustomobject]@{ ExitCode = $code; Text = ($lines -join "`n"); Lines = $lines }
}

function Read-ReleaseVersion([string]$Text) {
    $match = [regex]::Match($Text, 'AssemblyVersion\("(\d+\.\d+\.\d+(?:\.\d+)?)"\)')
    if (-not $match.Success) { throw 'AssemblyVersion is invalid.' }
    return [version]$match.Groups[1].Value
}

function Format-ReleaseVersion([version]$Value) {
    if ($Value.Major -ge 2000) { return ("{0}.{1:D2}.{2}" -f $Value.Major, $Value.Minor, $Value.Build) }
    return $Value.ToString()
}

try {
    $RepositoryPath = [IO.Path]::GetFullPath($RepositoryPath)
    $branch = (Invoke-RepositoryGit @("symbolic-ref", "--quiet", "--short", "HEAD")).Text
    $url = (Invoke-RepositoryGit @("remote", "get-url", $Remote)).Text
    if ($url -ne $ExpectedRepositoryUrl) { throw "Unexpected repository URL: $url" }
    $localVersion = Read-ReleaseVersion ([IO.File]::ReadAllText((Join-Path $RepositoryPath "Properties/AssemblyInfo.cs")))
    $localHead = (Invoke-RepositoryGit @("rev-parse", "HEAD")).Text

    Write-Host "[SOURCE] Dang kiem tra phien ban moi nhat tren Git..."
    $fetch = Invoke-RepositoryGit @("fetch", "--prune", $Remote)
    if ($fetch.Text) { Write-Host $fetch.Text }
    $refs = (Invoke-RepositoryGit @("for-each-ref",
        "--format=%(refname)|%(objectname)|%(committerdate:unix)|%(committerdate:iso-strict)", "refs/remotes/$Remote/")).Lines
    $pattern = "^refs/remotes/" + [regex]::Escape($Remote) + "/(?!HEAD$).+$"
    $candidates = @(
        foreach ($entry in $refs) {
            $fields = $entry -split '\|', 4
            if ($fields.Count -ne 4) { continue }
            $ref = $fields[0]
            $commit = $fields[1]
            if ($ref -notmatch $pattern) { continue }
            if ((Invoke-RepositoryGit @("cat-file", "-e", "${commit}:SX3 SCANER.csproj") -AllowFailure).ExitCode -ne 0) { continue }
            $props = Invoke-RepositoryGit @("show", "${commit}:Properties/AssemblyInfo.cs") -AllowFailure
            if ($props.ExitCode -ne 0) { continue }
            try { $version = Read-ReleaseVersion $props.Text }
            catch { Write-Host "[SOURCE] Bo qua $ref : Properties/AssemblyInfo.cs khong hop le."; continue }
            [pscustomobject]@{
                Ref = $ref
                Version = $version
                Commit = $commit
                Timestamp = [long]$fields[2]
                Date = $fields[3]
            }
        }
    )
    if ($candidates.Count -eq 0) { throw "No valid SX3 Scanner release branch was found on $Remote." }
    $latest = $candidates | Sort-Object @{Expression={$_.Version};Descending=$true},
        @{Expression={$_.Timestamp};Descending=$true}, @{Expression={$_.Ref};Descending=$true} | Select-Object -First 1
    $localDisplay = Format-ReleaseVersion $localVersion
    $latestDisplay = Format-ReleaseVersion $latest.Version

    # Report before any working-tree/branch changes.
    Write-Host "============================================================"
    Write-Host "[SOURCE] Phien ban tren may : V$localDisplay"
    Write-Host "[SOURCE] Nhanh tren may     : $branch"
    Write-Host "[SOURCE] Commit tren may    : $localHead"
    Write-Host "[SOURCE] Phien ban Git moi nhat: V$latestDisplay"
    Write-Host "[SOURCE] Nhanh moi nhat     : $($latest.Ref -replace '^refs/remotes/', '')"
    Write-Host "[SOURCE] Commit            : $($latest.Commit)"
    Write-Host "[SOURCE] Thoi gian commit  : $($latest.Date)"
    Write-Host "============================================================"
    if ($CheckOnly) { exit 0 }

    # Local work already based on the latest release can be built as-is.
    if ((Invoke-RepositoryGit @("merge-base", "--is-ancestor", $latest.Commit, "HEAD") -AllowFailure).ExitCode -eq 0) {
        Write-Host "[SOURCE] Code da cap nhat. Giu nguyen cac thay doi dang lam tren may."
        exit 0
    }
    if ($VerifyCurrent) {
        throw "Git da co source moi chua nam trong ban build nay. Da giu nguyen source va ban build; chay lai BUILD_FILE.cmd de cap nhat va build lai."
    }
    if ((Invoke-RepositoryGit @("merge-base", "--is-ancestor", "HEAD", $latest.Commit) -AllowFailure).ExitCode -ne 0) {
        throw "Local history diverged. Reconcile local commits before building; no changes discarded."
    }
    $dirty = (Invoke-RepositoryGit @("status", "--porcelain=v1", "--untracked-files=all")).Text
    $savedStash = $null
    if ($dirty) {
        $stash = Invoke-RepositoryGit @("stash", "push", "--include-untracked", "-m", "SX3 BUILD source synchronization")
        $savedStash = (Invoke-RepositoryGit @("rev-parse", "refs/stash")).Text
        Write-Host "[SOURCE] Saved local edits in stash $savedStash"
    }
    try {
        $merge = Invoke-RepositoryGit @("merge", "--ff-only", $latest.Commit)
        if ($merge.Text) { Write-Host $merge.Text }
    } finally {
        if ($savedStash) {
            $restore = Invoke-RepositoryGit @("stash", "apply", "--index", $savedStash) -AllowFailure
            if ($restore.ExitCode -ne 0) {
                throw "Cannot restore local edits cleanly. Build stopped; resolve conflicts. Backup stash retained: $savedStash`n$($restore.Text)"
            }
            $top = (Invoke-RepositoryGit @("rev-parse", "refs/stash")).Text
            if ($top -eq $savedStash) { $null = Invoke-RepositoryGit @("stash", "drop", "stash@{0}") }
        }
    }
    $updatedHead = (Invoke-RepositoryGit @("rev-parse", "HEAD")).Text
    if ($updatedHead -ne $latest.Commit) { throw "Updated source does not match the inspected commit." }
    Write-Host "[SOURCE] Da lay xong code. Nap lai BUILD_FILE.cmd tu source vua cap nhat."
    # The caller must restart the updated launcher, including updated build helpers.
    exit 10
}
catch {
    Write-Error $_
    exit 1
}
