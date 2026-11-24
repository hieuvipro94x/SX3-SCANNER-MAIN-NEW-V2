param(
    [string]$AssemblyInfoPath = (Join-Path (Split-Path -Parent $PSScriptRoot) 'Properties\AssemblyInfo.cs'),
    [switch]$Preview
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$text = [IO.File]::ReadAllText($AssemblyInfoPath)
$pattern = '(\[assembly:\s*AssemblyVersion\(")(\d+)\.(\d+)\.(\d+)(?:\.\d+)?("\)\])'
$matchesFound = [regex]::Matches($text, $pattern)
if ($matchesFound.Count -ne 1) { throw 'Expected exactly one AssemblyVersion attribute.' }
$current = $matchesFound[0]
$patch = [int]$current.Groups[4].Value + 1
if ($patch -gt 65534) { throw 'Patch exceeds the assembly version limit (65534).' }
$next = '{0}.{1}.{2}' -f $current.Groups[2].Value, $current.Groups[3].Value, $patch
foreach ($attribute in @('AssemblyVersion', 'AssemblyFileVersion', 'AssemblyInformationalVersion')) {
    $attributePattern = '(\[assembly:\s*' + $attribute + '\(")[^"]+("\)\])'
    if ([regex]::Matches($text, $attributePattern).Count -ne 1) {
        throw "Expected exactly one $attribute attribute."
    }
    $value = if ($attribute -eq 'AssemblyFileVersion') { "$next.0" } else { $next }
    $text = [regex]::Replace($text, $attributePattern, ('${1}' + $value + '${2}'))
}
if (-not $Preview) {
    $bytes = [IO.File]::ReadAllBytes($AssemblyInfoPath)
    $hasBom = $bytes.Length -ge 3 -and $bytes[0] -eq 239 -and $bytes[1] -eq 187 -and $bytes[2] -eq 191
    $encoding = New-Object Text.UTF8Encoding($hasBom)
    [IO.File]::WriteAllText($AssemblyInfoPath, $text, $encoding)
}
Write-Output $next
