param(
    [Parameter(Mandatory=$true)][string]$Source,
    [Parameter(Mandatory=$true)][string]$Output,
    [Parameter(Mandatory=$true)][string]$Version
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) { throw '.NET Framework C# compiler not found.' }
$work = Join-Path $root ('obj\SingleFile\' + [guid]::NewGuid().ToString('N'))
$stage = Join-Path $work 'payload'
New-Item -ItemType Directory -Path (Join-Path $stage 'x64') -Force | Out-Null
# Explicit runtime allowlist: do not package PDB/XML, old builds or databases.
$files = @(
    'SX3 SCANER.exe', 'SX3 SCANER.exe.config',
    'MaterialDesignColors.dll', 'MaterialDesignThemes.Wpf.dll',
    'Microsoft.Bcl.AsyncInterfaces.dll', 'Microsoft.Xaml.Behaviors.dll',
    'Newtonsoft.Json.dll', 'System.Buffers.dll', 'System.Data.SQLite.dll',
    'System.Memory.dll', 'System.Numerics.Vectors.dll',
    'System.Runtime.CompilerServices.Unsafe.dll', 'System.Threading.Tasks.Extensions.dll',
    'x64\SQLite.Interop.dll'
)
foreach ($relative in $files) {
    Copy-Item -LiteralPath (Join-Path $Source $relative) -Destination (Join-Path $stage $relative)
}
$payload = Join-Path $work 'payload.zip'
Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $payload -CompressionLevel Optimal
$info = Join-Path $work 'Version.cs'
@"
using System.Reflection;
[assembly: AssemblyTitle("SX3 Scanner")]
[assembly: AssemblyProduct("SX3 Scanner")]
[assembly: AssemblyVersion("$Version")]
[assembly: AssemblyFileVersion("$Version.0")]
[assembly: AssemblyInformationalVersion("$Version")]
"@ | Set-Content -LiteralPath $info -Encoding UTF8
New-Item -ItemType Directory -Path $Output -Force | Out-Null
$exe = Join-Path $Output 'SX3 SCANER.exe'
& $compiler /nologo /target:winexe /platform:x64 /optimize+ /debug- `
    "/out:$exe" "/win32icon:$(Join-Path $root 'scan.ico')" `
    "/win32manifest:$(Join-Path $root 'Properties\app.manifest')" `
    /reference:System.IO.Compression.dll /reference:System.Windows.Forms.dll `
    "/resource:$payload,SX3.Payload" (Join-Path $PSScriptRoot 'SingleFileLauncher.cs') $info
if ($LASTEXITCODE -ne 0) { throw 'Single EXE compilation failed.' }
$process = Start-Process -FilePath $exe -ArgumentList '--verify-package' -PassThru -WindowStyle Hidden
if (-not $process.WaitForExit(30000)) {
    $process.Kill()
    throw 'Single EXE verification timed out.'
}
if ($process.ExitCode -ne 0) { throw "Single EXE verification failed ($($process.ExitCode))." }
$rawSize = (Get-ChildItem -LiteralPath $stage -File -Recurse | Measure-Object Length -Sum).Sum
$exeSize = (Get-Item -LiteralPath $exe).Length
Write-Host ('Single EXE verified: {0:N2} MB (runtime before compression: {1:N2} MB)' -f ($exeSize/1MB), ($rawSize/1MB)) -ForegroundColor Green
