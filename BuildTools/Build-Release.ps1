param([switch]$NoPause)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot

function Invoke-Checked {
    param([string]$Executable, [string[]]$Arguments)
    & $Executable @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "Command failed (exit $LASTEXITCODE): $Executable"
    }
}

try {
    $project = Join-Path $projectRoot 'SX3 SCANER.csproj'
    if (-not (Test-Path -LiteralPath $project)) { throw 'Wrong project directory.' }

    $vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
    $msbuild = $null
    if (Test-Path -LiteralPath $vswhere) {
        $msbuild = & $vswhere -latest -prerelease -products '*' -requires Microsoft.Component.MSBuild -find 'MSBuild\**\Bin\MSBuild.exe' | Select-Object -First 1
    }
    if (-not $msbuild) {
        $command = Get-Command MSBuild.exe -ErrorAction SilentlyContinue
        if ($command) { $msbuild = $command.Source }
    }
    if (-not $msbuild) { throw 'Install Visual Studio Build Tools with .NET desktop build tools and .NET Framework 4.8 targeting pack.' }

    # Increment once per actual build, after source sync and BUILD confirmation.
    $version = & (Join-Path $PSScriptRoot 'Increment-Version.ps1')
    Write-Host "SX3 Scanner V$version - Release x64" -ForegroundColor Cyan

    # Restore packages.config dependencies using Visual Studio's NuGet restore.
    Invoke-Checked $msbuild @((Join-Path $projectRoot 'SX3 SCANER.sln'), '/t:Restore', '/p:RestorePackagesConfig=true', '/p:Configuration=Release', '/p:Platform=x64', '/verbosity:minimal', '/nologo')
    $testProject = Join-Path $projectRoot 'Tests\SX3.Scanner.Tests.csproj'
    Invoke-Checked $msbuild @($testProject, '/t:Rebuild', '/p:Configuration=Release', '/p:Platform=x64', '/verbosity:minimal', '/nologo')
    Invoke-Checked (Join-Path $projectRoot 'Tests\bin\Release\SX3.Scanner.Tests.exe') @()

    # Each build gets its own directory; older builds are retained.
    $output = Join-Path $projectRoot "PublishSingle\V$version"
    if (Test-Path -LiteralPath $output) { throw "Version output already exists: $output" }
    New-Item -ItemType Directory -Path $output -ErrorAction Stop | Out-Null
    $source = Join-Path $projectRoot 'bin\x64\Release'
    & (Join-Path $PSScriptRoot 'Pack-SingleFile.ps1') -Source $source -Output $output -Version $version
    $exe = Join-Path $output 'SX3 SCANER.exe'
    $files = @(Get-ChildItem -LiteralPath $output -Force)
    if ($files.Count -ne 1 -or $files[0].Name -ne 'SX3 SCANER.exe') {
        throw 'Single-file output must contain exactly one EXE.'
    }
    New-Item -ItemType Directory -Path (Join-Path $projectRoot 'artifacts') -Force | Out-Null
    @{ Version = $version; Output = $output; Exe = $exe; SHA256 = (Get-FileHash -LiteralPath $exe -Algorithm SHA256).Hash } |
        ConvertTo-Json | Set-Content -LiteralPath (Join-Path $projectRoot 'artifacts\last-build.json') -Encoding UTF8
    Write-Host "Build and tests passed. Single EXE: $exe" -ForegroundColor Green
    exit 0
}
catch {
    Write-Host "BUILD FAILED: $($_.Exception.Message)" -ForegroundColor Red
    exit 1
}
