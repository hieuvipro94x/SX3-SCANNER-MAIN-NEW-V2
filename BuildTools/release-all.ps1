param(
    [ValidateSet("Release")][string]$Configuration = "Release",
    [ValidateSet("x64")][string]$Platform = "x64",
    [string]$Repository = "hieuvipro94x/SX3-SCANNER-MAIN-NEW-V2",
    [Alias("Version")][string]$ReleaseVersion,
    [switch]$BuildOnly,
    [switch]$Draft
)

$ErrorActionPreference = "Stop"

function Info($Text) { Write-Host $Text -ForegroundColor Cyan }
function Ok($Text) { Write-Host $Text -ForegroundColor Green }
function Warn($Text) { Write-Host $Text -ForegroundColor Yellow }
function Fail($Text) { Write-Host $Text -ForegroundColor Red; throw $Text }
function Write-Step($Text) { Info "`n$Text" }

function Assert-LastExitCode($StepName) {
    if ($LASTEXITCODE -ne 0) {
        Fail "$StepName FAILED. ExitCode=$LASTEXITCODE"
    }
}

function Remove-ProjectBuildDirectory($Directory) {
    if (-not (Test-Path -LiteralPath $Directory)) { return }
    $resolvedTarget = [IO.Path]::GetFullPath($Directory)
    $allowedRoot = [IO.Path]::GetFullPath($ProjectRoot).TrimEnd('\') + '\'
    if (-not $resolvedTarget.StartsWith($allowedRoot, [StringComparison]::OrdinalIgnoreCase)) {
        Fail "Tu choi xoa thu muc ngoai project: $resolvedTarget"
    }
    Remove-Item -LiteralPath $resolvedTarget -Recurse -Force
}

function Find-InnoSetup {
    $paths = @(
        "C:\Program Files (x86)\Inno Setup 6\ISCC.exe",
        "C:\Program Files\Inno Setup 6\ISCC.exe"
    )

    foreach ($p in $paths) {
        if (Test-Path -LiteralPath $p) { return $p }
    }

    $cmd = Get-Command "ISCC.exe" -ErrorAction SilentlyContinue
    if ($cmd) { return $cmd.Source }

    Fail "Khong tim thay Inno Setup 6. Hay cai Inno Setup 6 truoc."
}

function Find-MSBuild {
    $paths = @(
        "C:\Program Files\Microsoft Visual Studio\18\Insiders\MSBuild\Current\Bin\MSBuild.exe",
        "C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe",
        "C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe",
        "C:\Program Files\Microsoft Visual Studio\2022\Professional\MSBuild\Current\Bin\MSBuild.exe",
        "C:\Program Files\Microsoft Visual Studio\2022\Enterprise\MSBuild\Current\Bin\MSBuild.exe",
        "C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin\MSBuild.exe"
    )

    foreach ($p in $paths) {
        if (Test-Path -LiteralPath $p) { return $p }
    }

    $cmd = Get-Command "MSBuild.exe" -ErrorAction SilentlyContinue
    if ($cmd) { return $cmd.Source }

    Fail "Khong tim thay MSBuild. Hay cai Visual Studio/Build Tools voi .NET desktop build tools."
}

function Find-GitHubCLI {
    $cmd = Get-Command "gh.exe" -ErrorAction SilentlyContinue
    if ($cmd) { return $cmd.Source }

    $paths = @(
        (Join-Path $env:ProgramFiles "GitHub CLI\gh.exe"),
        (Join-Path ${env:ProgramFiles(x86)} "GitHub CLI\gh.exe"),
        (Join-Path $env:LOCALAPPDATA "Programs\GitHub CLI\gh.exe"),
        (Join-Path $env:LOCALAPPDATA "Microsoft\WinGet\Links\gh.exe")
    )

    foreach ($p in $paths) {
        if ($p -and (Test-Path -LiteralPath $p)) { return $p }
    }

    Fail "Khong tim thay GitHub CLI (gh.exe). Hay cai GitHub CLI truoc."
}

function Find-Git {
    $cmd = Get-Command "git.exe" -ErrorAction SilentlyContinue
    if ($cmd) { return $cmd.Source }
    Fail "Khong tim thay Git. Hay cai Git truoc."
}

function Get-ProjectRoot {
    $scriptDir = $PSScriptRoot

    if ((Split-Path -Leaf $scriptDir) -ieq "ká»‹ch báº£n") {
        $scriptDir = Split-Path -Parent $scriptDir
    }

    if ((Split-Path -Leaf $scriptDir) -ieq "BuildTools") {
        return Split-Path -Parent $scriptDir
    }

    return $scriptDir
}

function Get-GitRoot($ProjectRoot, $GitExe) {
    $gitRoot = & $GitExe -C $ProjectRoot rev-parse --show-toplevel 2>$null
    if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($gitRoot)) {
        Fail "Thu muc project khong phai Git repository: $ProjectRoot"
    }

    return $gitRoot.Trim()
}

function Read-VersionFromAssemblyInfo($ProjectRoot) {
    $assemblyInfo = Join-Path $ProjectRoot "Properties\AssemblyInfo.cs"
    if (-not (Test-Path -LiteralPath $assemblyInfo)) {
        Fail "Khong tim thay Properties\AssemblyInfo.cs."
    }

    $text = Get-Content -LiteralPath $assemblyInfo -Raw -Encoding UTF8

    $patterns = @(
        'AssemblyInformationalVersion\("([^"+]+)(?:\+[^"+]+)?"\)',
        'AssemblyFileVersion\("([^"]+)"\)',
        'AssemblyVersion\("([^"]+)"\)'
    )

    foreach ($pattern in $patterns) {
        $match = [regex]::Match($text, $pattern)
        if ($match.Success) {
            $raw = $match.Groups[1].Value.Trim()
            $parts = $raw.Split(".")
            if ($parts.Length -ge 3) {
                return "$($parts[0]).$($parts[1]).$($parts[2])"
            }
        }
    }

    Fail "Khong doc duoc version tu AssemblyInfo.cs. Can co AssemblyInformationalVersion/AssemblyFileVersion/AssemblyVersion."
}

function Get-ReleaseNote($BuildToolsDir, $Version) {
    $notePath = Join-Path $BuildToolsDir "release-note.txt"

    if (-not (Test-Path -LiteralPath $notePath)) {
        $defaultText = @"
SX3 SCANNER RELEASE NOTE

Version: $Version
Build time: $(Get-Date -Format "yyyy-MM-dd HH:mm:ss")

Noi dung cap nhat:
- Cap nhat phan mem.

Huong dan cai dat:
1. Tat SX3 Scanner neu dang mo.
2. Chay file SX3ScannerSetup-$Version.exe.
3. Chon Next de cai dat.
"@
        New-Item -ItemType Directory -Force -Path $BuildToolsDir | Out-Null
        $defaultText | Out-File -LiteralPath $notePath -Encoding UTF8
        Ok "Da tao file release-note.txt: $notePath"
    }

    $noteText = Get-Content -LiteralPath $notePath -Raw -Encoding UTF8
    if ([string]::IsNullOrWhiteSpace($noteText)) {
        Fail "BuildTools\release-note.txt dang rong. Hay nhap noi dung release note."
    }

    return @{
        Path = $notePath
        Text = $noteText.Trim()
    }
}

function Test-GitHubReleaseExists($GitHubCLI, $Repository, $Tag) {
    $previousErrorActionPreference = $ErrorActionPreference
    try {
        $ErrorActionPreference = "Continue"
        & $GitHubCLI release view $Tag --repo $Repository *> $null
        $exitCode = $LASTEXITCODE
    }
    finally {
        $ErrorActionPreference = $previousErrorActionPreference
    }

    return $exitCode -eq 0
}

function Test-RemoteTagExists($GitExe, $GitRoot, $Tag) {
    $output = & $GitExe -C $GitRoot ls-remote --tags origin "refs/tags/$Tag" 2>&1
    $exitCode = $LASTEXITCODE

    if ($exitCode -ne 0) {
        Fail "Khong kiem tra duoc remote tag $Tag. Chi tiet: $($output | Out-String)"
    }

    return -not [string]::IsNullOrWhiteSpace(($output | Out-String))
}

function New-ReleaseManifest($InstallerFile, $ManifestFile, $Version, $Repository) {
    $hashAlgorithm = [Security.Cryptography.SHA256]::Create()
    $installerStream = [IO.File]::OpenRead($InstallerFile)
    try {
        $sha256 = ([BitConverter]::ToString($hashAlgorithm.ComputeHash($installerStream))).Replace('-', '').ToLowerInvariant()
    }
    finally {
        $installerStream.Dispose()
        $hashAlgorithm.Dispose()
    }
    $installerName = Split-Path -Leaf $InstallerFile

    $manifest = @"
SX3 Scanner Release Manifest
Version: $Version
Repository: $Repository
Installer: $installerName
SHA256: $sha256
Build time: $(Get-Date -Format "yyyy-MM-dd HH:mm:ss")
"@

    $manifest | Out-File -LiteralPath $ManifestFile -Encoding UTF8
    return $sha256
}

function Publish-GitHubRelease(
    $GitRoot,
    $GitExe,
    $GitHubCLI,
    $Repository,
    $Version,
    $InstallerFile,
    $ReleaseNoteFile,
    $ManifestFile
) {
    $tag = "v$Version"
    $title = "SX3 Scanner $Version"

    Write-Step "[5/9] Kiem tra release/tag da ton tai..."

    if (Test-GitHubReleaseExists -GitHubCLI $GitHubCLI -Repository $Repository -Tag $tag) {
        Fail "Release $tag da ton tai. Hay tang version trong app."
    }

    if (Test-RemoteTagExists -GitExe $GitExe -GitRoot $GitRoot -Tag $tag) {
        Fail "Git tag $tag da ton tai tren origin. Script KHONG ghi de tag cu. Hay tang version app truoc khi dong goi."
    }

    & $GitExe -C $GitRoot show-ref --verify --quiet "refs/tags/$tag"
    $localTagExitCode = $LASTEXITCODE
    if ($localTagExitCode -eq 0) {
        Fail "Local tag $tag da ton tai. Script KHONG xoa tag cu. Hay tang version app hoac xoa tag local thu cong neu chac chan."
    }
    elseif ($localTagExitCode -ne 1) {
        Fail "Khong kiem tra duoc local tag $tag. ExitCode=$localTagExitCode"
    }

    Write-Step "[6/9] Commit source code neu co thay doi..."
    & $GitExe -C $GitRoot add .
    Assert-LastExitCode "git add"

    & $GitExe -C $GitRoot diff --cached --quiet
    $diffExitCode = $LASTEXITCODE
    if ($diffExitCode -eq 0) {
        Warn "Khong co thay doi de commit. Tiep tuc tao draft release."
    }
    elseif ($diffExitCode -eq 1) {
        & $GitExe -C $GitRoot commit -m "Release $tag"
        Assert-LastExitCode "git commit"
    }
    else {
        Fail "Khong kiem tra duoc thay doi Git. ExitCode=$diffExitCode"
    }

    Write-Step "[7/9] Push source code..."
    & $GitExe -C $GitRoot push origin "HEAD:refs/heads/$ReleaseBranch"
    Assert-LastExitCode "git push"

    Write-Step "[8/9] Tao tag moi $tag..."
    & $GitExe -C $GitRoot tag -a $tag -m "Release $tag"
    Assert-LastExitCode "git tag -a $tag"

    & $GitExe -C $GitRoot push origin $tag
    Assert-LastExitCode "git push origin $tag"

    Write-Step "[9/9] Tao GitHub Release va upload assets..."
    $releaseArgs = @(
        "release", "create", $tag, $InstallerFile, $ReleaseNoteFile, $ManifestFile,
        "--repo", $Repository, "--title", $title, "--notes-file", $ReleaseNoteFile,
        "--verify-tag"
    )
    if ($Draft) { $releaseArgs += "--draft" }
    & $GitHubCLI @releaseArgs
    Assert-LastExitCode "gh release create $tag"

}

$ProjectRoot = Get-ProjectRoot
$BuildToolsDir = Join-Path $ProjectRoot "BuildTools"
$ReleaseOutputDir = Join-Path $ProjectRoot "ReleaseOutput"
$OutputDir = Join-Path $ProjectRoot "InstallerOutput"
$PackageDir = Join-Path $OutputDir "PackageFiles"
$BinRelease = Join-Path $ProjectRoot "bin\$Platform\$Configuration"
$ProjectFile = Join-Path $ProjectRoot "SX3 SCANER.csproj"

$CurrentVersion = Read-VersionFromAssemblyInfo $ProjectRoot
$Version = $CurrentVersion
if (-not [string]::IsNullOrWhiteSpace($ReleaseVersion)) {
    $Version = $ReleaseVersion.Trim().TrimStart('v', 'V')
}
elseif (-not $BuildOnly) {
    $parsedCurrent = [version]$CurrentVersion
    $Version = "{0}.{1}.0" -f $parsedCurrent.Major, ($parsedCurrent.Minor + 1)
}
if ($Version -notmatch '^\d+\.\d+\.\d+$' -or
    @($Version.Split('.') | Where-Object { [long]$_ -gt 65534 }).Count -gt 0) {
    Fail "Version phai co dang 10.48.0, moi thanh phan tu 0 den 65534."
}
if (-not $BuildOnly -and ([version]$Version) -le ([version]$CurrentVersion)) {
    Fail "Version phat hanh $Version phai lon hon version hien tai $CurrentVersion."
}
$ReleaseNote = Get-ReleaseNote -BuildToolsDir $BuildToolsDir -Version $Version

$InstallerFileName = "SX3ScannerSetup-$Version.exe"
$InstallerFile = Join-Path $OutputDir $InstallerFileName
$ReleaseNoteOutputFile = Join-Path $OutputDir "release-note.txt"
$ManifestFile = Join-Path $OutputDir "release-manifest.txt"
$IssFile = Join-Path $OutputDir "SX3ScannerSetup.iss"
$ReleaseUrl = "https://github.com/$Repository/releases/tag/v$Version"

$GitExe = Find-Git
$GitHubCLI = $null
if (-not $BuildOnly) { $GitHubCLI = Find-GitHubCLI }
$MSBuild = Find-MSBuild
$InnoCompiler = Find-InnoSetup

Ok "Git: $GitExe"
Ok "GitHub CLI: $GitHubCLI"
Ok "MSBuild: $MSBuild"
Ok "Inno Setup: $InnoCompiler"

$GitRoot = Get-GitRoot -ProjectRoot $ProjectRoot -GitExe $GitExe
Ok "Git repository: $GitRoot"

if (-not $BuildOnly) {
    Info "Kiem tra dang nhap GitHub CLI..."
    & $GitHubCLI auth status
    Assert-LastExitCode "GitHub login (neu chua dang nhap, chay gh auth login)"
    $originUrl = & $GitExe -C $GitRoot remote get-url origin
    Assert-LastExitCode "git remote get-url origin"
    $normalizedOrigin = ($originUrl.Trim() -replace '\.git$', '').TrimEnd('/')
    if ($normalizedOrigin -notin @("https://github.com/$Repository", "git@github.com:$Repository")) {
        Fail "origin khong khop repository $Repository : $originUrl"
    }
    $ReleaseBranch = & $GitExe -C $GitRoot symbolic-ref --quiet --short HEAD
    Assert-LastExitCode "Nhanh Git hien tai (khong phat hanh tu detached HEAD)"
    & $GitExe -C $GitRoot fetch origin
    Assert-LastExitCode "git fetch origin"
    & $GitExe -C $GitRoot merge-base --is-ancestor "origin/$ReleaseBranch" HEAD
    Assert-LastExitCode "Nhanh local phai chua code moi nhat tu origin/$ReleaseBranch"
    $tag = "v$Version"
    if ((Test-GitHubReleaseExists $GitHubCLI $Repository $tag) -or
        (Test-RemoteTagExists $GitExe $GitRoot $tag)) {
        Fail "Release/tag $tag da ton tai. Chay lai voi -Version khac."
    }
    & $GitExe -C $GitRoot show-ref --verify --quiet "refs/tags/$tag"
    if ($LASTEXITCODE -eq 0) { Fail "Local tag $tag da ton tai." }
    if ($LASTEXITCODE -ne 1) { Fail "Khong kiem tra duoc local tag $tag." }
}

# Only change the source version once prerequisites and remote checks pass.
if ($Version -ne $CurrentVersion) {
    $assemblyInfoPath = Join-Path $ProjectRoot "Properties\AssemblyInfo.cs"
    $assemblyText = Get-Content -LiteralPath $assemblyInfoPath -Raw -Encoding UTF8
    $assemblyText = $assemblyText -replace 'AssemblyVersion\("[^\"]+"\)', "AssemblyVersion(`"$Version`")"
    $assemblyText = $assemblyText -replace 'AssemblyFileVersion\("[^\"]+"\)', "AssemblyFileVersion(`"$Version.0`")"
    $assemblyText = $assemblyText -replace 'AssemblyInformationalVersion\("[^\"]+"\)', "AssemblyInformationalVersion(`"$Version`")"
    [IO.File]::WriteAllText($assemblyInfoPath, $assemblyText, [Text.UTF8Encoding]::new($false))
}

Info "====================================="
Info " SX3 SCANNER X64 BUILD + PACKAGE"
Info " Version from AssemblyInfo: $Version"
Info " BuildOnly=$BuildOnly | Draft=$Draft | Khong ghi de release/tag cu"
Info "====================================="
Info "ProjectRoot: $ProjectRoot"
Info "Release source: $BinRelease"
Info "Output: $OutputDir"
Info "Repository: $Repository"

Info "`nDon artifact cu truoc khi build..."
foreach ($dir in @($ReleaseOutputDir, $OutputDir)) {
    Remove-ProjectBuildDirectory $dir
}

Write-Step "[1/9] Build $Configuration|$Platform..."
& $MSBuild (Join-Path $ProjectRoot "SX3 SCANER.sln") /t:Restore /p:RestorePackagesConfig=true /p:Configuration=$Configuration /p:Platform=$Platform /v:minimal /nologo
Assert-LastExitCode "NuGet restore"
& $MSBuild $ProjectFile /t:Rebuild /p:Configuration=$Configuration /p:Platform=$Platform /m /v:minimal /nologo
Assert-LastExitCode "MSBuild $Configuration|$Platform"

$TestProject = Join-Path $ProjectRoot "Tests\SX3.Scanner.Tests.csproj"
& $MSBuild $TestProject /p:Configuration=$Configuration /p:Platform=$Platform /v:minimal /nologo
Assert-LastExitCode "Build tests"
& (Join-Path $ProjectRoot "Tests\bin\$Configuration\SX3.Scanner.Tests.exe")
Assert-LastExitCode "Scanner tests"


$Exe = Join-Path $BinRelease "SX3 SCANER.exe"
if (-not (Test-Path -LiteralPath $Exe)) {
    Fail "Khong tim thay file EXE x64: $Exe."
}

# Am thanh scan OK/NG da duoc nhung vao EXE/DLL bang Build Action = Resource.
# Khong kiem tra file Sounds\*.wav roi trong bin Release nua, vi ban cai dat khong can cac file am thanh roi.
Ok "Am thanh scan OK/NG dang dung dang nhung Resource trong EXE/DLL; bo qua kiem tra file Sounds roi."

New-Item -ItemType Directory -Force -Path $OutputDir | Out-Null
New-Item -ItemType Directory -Force -Path $PackageDir | Out-Null

Write-Step "[2/9] Copy bin\$Platform\$Configuration vao PackageFiles..."
Copy-Item -Path (Join-Path $BinRelease "*") -Destination $PackageDir -Recurse -Force

# Release package chi chua runtime files. Symbol, XML documentation va native
# binary sai kien truc khong can thiet cho ban x64 chinh thuc.
Get-ChildItem -LiteralPath $PackageDir -Recurse -File |
    Where-Object { $_.Extension -in @(".pdb", ".xml") } |
    Remove-Item -Force

$PackageX86 = Join-Path $PackageDir "x86"
if (Test-Path -LiteralPath $PackageX86) {
    Remove-ProjectBuildDirectory $PackageX86
}

# Am thanh OK/NG phai nam trong EXE/DLL dang Embedded Resource.
# Neu project van copy Sounds\*.wav ra bin Release thi xoa khoi bo cai dat de nguoi dung khong thay/sua/xoa duoc.
$PackageSoundsDir = Join-Path $PackageDir "Sounds"
if (Test-Path -LiteralPath $PackageSoundsDir) {
    Remove-ProjectBuildDirectory $PackageSoundsDir
    Ok "Da xoa thu muc Sounds khoi package vi am thanh da nhung trong EXE/DLL."
}

$LooseSoundFiles = Get-ChildItem -LiteralPath $PackageDir -Recurse -File -ErrorAction SilentlyContinue |
    Where-Object { $_.Name -in @("OK.wav", "NG.wav") }
if ($LooseSoundFiles) {
    $LooseSoundFiles | Remove-Item -Force
    Ok "Da xoa file OK.wav/NG.wav roi khoi package."
}

$LeakedSoundFiles = Get-ChildItem -LiteralPath $PackageDir -Recurse -File -ErrorAction SilentlyContinue |
    Where-Object { $_.Name -in @("OK.wav", "NG.wav") -or $_.FullName -match "\\Sounds\\" }
if ($LeakedSoundFiles) {
    Fail "Package van con file sound roi: $($LeakedSoundFiles.FullName -join ', ')"
}

$UnexpectedReleaseFiles = Get-ChildItem -LiteralPath $PackageDir -Recurse -File |
    Where-Object {
        $_.Name -like "FIX_*.ps1" -or
        $_.Name -like "README_FIX_*" -or
        $_.Extension -in @(".log", ".tmp", ".bak")
    }
if ($UnexpectedReleaseFiles) {
    Fail "Package con file tam/log/backup: $($UnexpectedReleaseFiles.FullName -join ', ')"
}

Ok "Package dung am thanh nhung Embedded Resource; installer se khong cai Sounds\OK.wav / Sounds\NG.wav ra thu muc app."

Write-Step "[3/9] Copy release-note.txt..."
Copy-Item -LiteralPath $ReleaseNote.Path -Destination (Join-Path $PackageDir "release-note.txt") -Force
Copy-Item -LiteralPath $ReleaseNote.Path -Destination (Join-Path $PackageDir "RELEASE_NOTES.txt") -Force
Copy-Item -LiteralPath $ReleaseNote.Path -Destination $ReleaseNoteOutputFile -Force

Write-Step "[4/9] Tao Inno Setup script va dong goi installer..."
$IconLine = ""
$IconSourceLine = ""
$IconPath = Join-Path $ProjectRoot "scan.ico"
if (Test-Path -LiteralPath $IconPath) {
    $EscapedIconPath = $IconPath.Replace('"', '""')
    $IconLine = "SetupIconFile=$EscapedIconPath"
    $IconSourceLine = "Source: `"$EscapedIconPath`"; DestDir: `"{app}`"; DestName: `"scan.ico`"; Flags: ignoreversion"
}

$EscapedOutputDir = $OutputDir.Replace('"', '""')
$EscapedPackageDir = $PackageDir.Replace('"', '""')

$IssContent = @"
#define MyAppName "SX3 Scanner"
#define MyAppVersion "$Version"
#define MyAppPublisher "JBZVN"
#define MyAppExeName "SX3 SCANER.exe"

[Setup]
AppId={{A6E812B1-9F30-4D3C-A9E2-0A0B0C0D0E0F}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL=https://github.com/$Repository
AppSupportURL=https://github.com/$Repository/issues
DefaultDirName={autopf}\JBZVN\SX3 Scanner
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
DisableDirPage=no
OutputDir=$EscapedOutputDir
OutputBaseFilename=SX3ScannerSetup-{#MyAppVersion}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=admin
UsedUserAreasWarning=no
CloseApplications=yes
RestartApplications=no
SetupLogging=yes
UninstallDisplayName={#MyAppName}
UninstallDisplayIcon={app}\{#MyAppExeName}
VersionInfoVersion={#MyAppVersion}.0
VersionInfoCompany={#MyAppPublisher}
VersionInfoDescription={#MyAppName}
VersionInfoProductName={#MyAppName}
VersionInfoProductVersion={#MyAppVersion}
VersionInfoCopyright=Copyright Â© 2026 NAM
$IconLine

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Additional icons:"; Flags: unchecked

[Dirs]
Name: "{commonappdata}\JBZVN\SX3 Scanner"; Permissions: users-modify
Name: "{commonappdata}\JBZVN\SX3 Scanner\config"; Permissions: users-modify

[InstallDelete]
Type: filesandordirs; Name: "{app}\AnnouncementServer"
Type: files; Name: "{userstartup}\SX3 SCANER.lnk"
Type: files; Name: "{commonstartup}\SX3 SCANER.lnk"
Type: files; Name: "{commondesktop}\SX3 Scanner.lnk"
Type: files; Name: "{userdesktop}\SX3 Scanner.lnk"

[Files]
Source: "$EscapedPackageDir\*"; DestDir: "{app}"; Excludes: "database.db,product.db,database.db-wal,database.db-shm,product.db-wal,product.db-shm,database.db-journal,product.db-journal"; Flags: ignoreversion
Source: "$EscapedPackageDir\x64\*"; DestDir: "{app}\x64"; Flags: ignoreversion recursesubdirs createallsubdirs
$IconSourceLine
; Am thanh OK/NG da nhung vao EXE/DLL, khong copy file Sounds\*.wav roi vao thu muc cai dat.

[Icons]
Name: "{group}\SX3 Scanner"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\scan.ico"; IconIndex: 0
Name: "{group}\Release note"; Filename: "{app}\release-note.txt"
Name: "{commondesktop}\SX3 Scanner"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\scan.ico"; IconIndex: 0

[Run]
Filename: "{sys}\schtasks.exe"; Parameters: "/Create /F /TN ""SX3 Scanner"" /TR ""{app}\{#MyAppExeName}"" /SC ONLOGON /RL HIGHEST"; Flags: runhidden waituntilterminated
Filename: "{app}\{#MyAppExeName}"; Description: "Open SX3 Scanner"; Flags: nowait postinstall skipifsilent

[Code]
procedure KillProcessByName(FileName: string);
var
  ResultCode: Integer;
begin
  Exec(ExpandConstant('{sys}\taskkill.exe'), '/F /IM "' + FileName + '"', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
end;

function InitializeSetup(): Boolean;
begin
  KillProcessByName('SX3 SCANER.exe');
  Sleep(2000);
  Result := True;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  KillProcessByName('SX3 SCANER.exe');
  Sleep(2000);
  Result := '';
end;

"@

$IssContent | Out-File -LiteralPath $IssFile -Encoding UTF8

& $InnoCompiler $IssFile
Assert-LastExitCode "Inno Setup build"

if (-not (Test-Path -LiteralPath $InstallerFile)) {
    Fail "Khong tim thay installer: $InstallerFile"
}

$Sha256 = New-ReleaseManifest -InstallerFile $InstallerFile -ManifestFile $ManifestFile -Version $Version -Repository $Repository
if ($BuildOnly) {
    Ok "BUILD THANH CONG - KHONG COMMIT/PUSH GITHUB"
    Ok "Installer: $InstallerFile"
    Ok "SHA256: $Sha256"
    exit 0
}

Publish-GitHubRelease `
    -GitRoot $GitRoot `
    -GitExe $GitExe `
    -GitHubCLI $GitHubCLI `
    -Repository $Repository `
    -Version $Version `
    -InstallerFile $InstallerFile `
    -ReleaseNoteFile $ReleaseNoteOutputFile `
    -ManifestFile $ManifestFile

Ok "`n====================================="
Ok "GITHUB RELEASE DA DUOC TAO THANH CONG (Draft=$Draft)"
Ok "Version: $Version"
Ok "Release URL: $ReleaseUrl"
Ok "Assets:"
Ok "- $InstallerFileName"
Ok "- release-note.txt"
Ok "- release-manifest.txt"
Ok "SHA256: $Sha256"
Ok "====================================="
