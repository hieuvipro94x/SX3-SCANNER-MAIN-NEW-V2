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
    $stamp = Get-Date -Format 'yyyyMMdd_HHmmss_fff'
    $output = Join-Path $projectRoot "artifacts\V$version\SX3Scanner_${stamp}"
    New-Item -ItemType Directory -Path $output -ErrorAction Stop | Out-Null
    $source = Join-Path $projectRoot 'bin\x64\Release'
    foreach ($file in Get-ChildItem -LiteralPath $source -File) {
        if ($file.Extension -in @('.exe', '.dll', '.config') -and $file.Name -notmatch '^Markdig\.dll$') {
            Copy-Item -LiteralPath $file.FullName -Destination $output
        }
    }
    $nativeOutput = Join-Path $output 'x64'
    New-Item -ItemType Directory -Path $nativeOutput | Out-Null
    Copy-Item -LiteralPath (Join-Path $source 'x64\SQLite.Interop.dll') -Destination $nativeOutput
    if (-not (Test-Path -LiteralPath (Join-Path $output 'SX3 SCANER.exe'))) { throw 'Application EXE missing.' }

    @(
        "SX3 Scanner V$version - Release x64"
        'Requires Windows x64 and .NET Framework 4.8.'
        'Keep the EXE, config, DLLs and x64 directory together.'
        'Data directory: %LOCALAPPDATA%\SX3_SCANER. Missing databases are created on first run.'
        'This build does not include online update or announcement services.'
    ) | Set-Content -LiteralPath (Join-Path $output 'README.txt') -Encoding UTF8
    $hashes = Get-ChildItem -LiteralPath $output -File -Recurse | ForEach-Object {
        [PSCustomObject]@{
            File = $_.FullName.Substring($output.Length + 1)
            SHA256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash
        }
    }
    $hashes | Export-Csv -LiteralPath (Join-Path $output 'SHA256.csv') -NoTypeInformation -Encoding UTF8
    $zip = "$output.zip"
    Compress-Archive -Path (Join-Path $output '*') -DestinationPath $zip -CompressionLevel Optimal
    @{ Version = $version; Output = $output; Zip = $zip } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $projectRoot 'artifacts\last-build.json') -Encoding UTF8
    Write-Host "Build and tests passed. Output: $output" -ForegroundColor Green
    Write-Host "ZIP: $zip"
    exit 0
}
catch {
    Write-Host "BUILD FAILED: $($_.Exception.Message)" -ForegroundColor Red
    exit 1
}
