Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$build = Get-Content -LiteralPath (Join-Path $root 'artifacts\last-build.json') -Raw | ConvertFrom-Json
$expectedOutput = Join-Path $root ("PublishSingle\V" + $build.Version)
if ($build.Output -ne $expectedOutput) { throw 'EXE is not in the version-specific PublishSingle folder.' }
$entries = @(Get-ChildItem -LiteralPath $build.Output -Force)
if ($entries.Count -ne 1 -or $entries[0].Name -ne 'SX3 SCANER.exe') {
    throw 'Delivery must contain exactly one EXE.'
}
if ((Get-FileHash -LiteralPath $build.Exe -Algorithm SHA256).Hash -ne $build.SHA256) {
    throw 'EXE hash does not match build manifest.'
}
$version = [Diagnostics.FileVersionInfo]::GetVersionInfo($build.Exe).FileVersion
if ($version -ne "$($build.Version).0") { throw "Incorrect EXE version: $version" }
$isolated = Join-Path $env:TEMP ('SX3 standalone test ' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $isolated | Out-Null
$copy = Join-Path $isolated 'SX3 standalone.exe'
Copy-Item -LiteralPath $build.Exe -Destination $copy
# Smoke mode loads the application's WPF resources and executes in-memory SQLite;
# it does not initialize operational databases, register startup or open the scan UI.
$processes = @(1..2 | ForEach-Object {
    Start-Process -FilePath $copy -ArgumentList '--verify-package' -WorkingDirectory $isolated -PassThru -WindowStyle Hidden
})
foreach ($process in $processes) {
    if (-not $process.WaitForExit(30000)) {
        $process.Kill()
        throw 'Standalone smoke test timed out.'
    }
    if ($process.ExitCode -ne 0) { throw "Standalone smoke test failed: $($process.ExitCode)" }
}
if (@(Get-ChildItem -LiteralPath $isolated -Force).Count -ne 1) { throw 'Runtime files leaked beside the EXE.' }
Write-Host 'PASS: single EXE, version, SHA256, renamed standalone launch, concurrent extraction, WPF resources and native SQLite'
Write-Host "Standalone test copy: $copy"
