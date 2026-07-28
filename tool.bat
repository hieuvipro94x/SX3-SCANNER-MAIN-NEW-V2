@echo off
setlocal EnableExtensions EnableDelayedExpansion
chcp 65001 >nul
title .NET Single EXE Publisher

echo ============================================================
echo          .NET SINGLE EXE PUBLISH TOOL
echo ============================================================
echo.

rem ------------------------------------------------------------
rem 1. Kiem tra he dieu hanh
rem ------------------------------------------------------------
if /I not "%OS%"=="Windows_NT" (
    echo [LOI] Tool nay chi chay tren Windows.
    pause
    exit /b 1
)

rem ------------------------------------------------------------
rem 2. Kiem tra .NET SDK
rem ------------------------------------------------------------
where dotnet >nul 2>&1
if errorlevel 1 (
    echo [LOI] Khong tim thay lenh "dotnet".
    echo Hay cai .NET SDK, sau do mo lai CMD va chay tool.
    pause
    exit /b 1
)

set "DOTNET_VERSION=Khong xac dinh"
for /f "delims=" %%V in ('dotnet --version 2^>nul') do set "DOTNET_VERSION=%%V"

rem ------------------------------------------------------------
rem 3. Phat hien kien truc Windows
rem PROCESSOR_ARCHITEW6432 giup phat hien Windows 64-bit khi
rem tool dang chay trong CMD 32-bit.
rem ------------------------------------------------------------
set "RAW_ARCH=%PROCESSOR_ARCHITECTURE%"
if defined PROCESSOR_ARCHITEW6432 set "RAW_ARCH=%PROCESSOR_ARCHITEW6432%"

set "OS_ARCH=unknown"
if /I "%RAW_ARCH%"=="AMD64" set "OS_ARCH=x64"
if /I "%RAW_ARCH%"=="x86"   set "OS_ARCH=x86"
if /I "%RAW_ARCH%"=="ARM64" set "OS_ARCH=arm64"

echo [THONG TIN]
echo   He dieu hanh : Windows
echo   Kien truc OS : %OS_ARCH%
echo   .NET SDK     : %DOTNET_VERSION%
echo.

rem ------------------------------------------------------------
rem 4. Tim project .csproj
rem Co the chay:
rem   tool.bat
rem hoac:
rem   tool.bat "D:\DuAn\App\App.csproj"
rem ------------------------------------------------------------
set "PROJECT=%~1"

if defined PROJECT goto VALIDATE_PROJECT

set /a PROJECT_COUNT=0
for /f "delims=" %%F in ('dir /b /s /a-d "*.csproj" 2^>nul') do (
    set /a PROJECT_COUNT+=1
    set "PROJECT_!PROJECT_COUNT!=%%~fF"
)

if !PROJECT_COUNT! EQU 0 (
    echo [LOI] Khong tim thay file .csproj trong thu muc hien tai:
    echo   %CD%
    echo.
    echo Hay dat tool.bat trong thu muc project, hoac chay:
    echo   tool.bat "D:\DuongDan\TenProject.csproj"
    pause
    exit /b 1
)

if !PROJECT_COUNT! EQU 1 (
    set "PROJECT=!PROJECT_1!"
    goto VALIDATE_PROJECT
)

echo Tim thay !PROJECT_COUNT! project:
echo.
for /L %%I in (1,1,!PROJECT_COUNT!) do echo   %%I. !PROJECT_%%I!
echo.
set "PROJECT_CHOICE="
set /p "PROJECT_CHOICE=Nhap so thu tu project can build: "

for /f "delims=0123456789" %%A in ("!PROJECT_CHOICE!") do set "PROJECT_CHOICE="
if not defined PROJECT_CHOICE (
    echo [LOI] Lua chon khong hop le.
    pause
    exit /b 1
)
if !PROJECT_CHOICE! LSS 1 (
    echo [LOI] Lua chon khong hop le.
    pause
    exit /b 1
)
if !PROJECT_CHOICE! GTR !PROJECT_COUNT! (
    echo [LOI] Lua chon khong hop le.
    pause
    exit /b 1
)

for %%I in (!PROJECT_CHOICE!) do set "PROJECT=!PROJECT_%%I!"

:VALIDATE_PROJECT
set "PROJECT=!PROJECT:"=!"
if not exist "!PROJECT!" (
    echo [LOI] Khong tim thay project:
    echo   !PROJECT!
    pause
    exit /b 1
)

for %%F in ("!PROJECT!") do (
    set "PROJECT=%%~fF"
    set "PROJECT_DIR=%%~dpF"
    set "PROJECT_NAME=%%~nF"
)

echo.
echo Project da chon:
echo   !PROJECT!
echo.

rem ------------------------------------------------------------
rem 5. Chon kien truc dich
rem ------------------------------------------------------------
echo Chon kien truc file EXE:
echo   1. Tu dong theo Windows hien tai [%OS_ARCH%]
echo   2. Windows x64
echo   3. Windows x86
echo   4. Windows ARM64
echo.
set "ARCH_CHOICE="
set /p "ARCH_CHOICE=Lua chon [1]: "
if not defined ARCH_CHOICE set "ARCH_CHOICE=1"

set "TARGET_ARCH="
if "!ARCH_CHOICE!"=="1" set "TARGET_ARCH=%OS_ARCH%"
if "!ARCH_CHOICE!"=="2" set "TARGET_ARCH=x64"
if "!ARCH_CHOICE!"=="3" set "TARGET_ARCH=x86"
if "!ARCH_CHOICE!"=="4" set "TARGET_ARCH=arm64"

if not defined TARGET_ARCH (
    echo [LOI] Lua chon kien truc khong hop le.
    pause
    exit /b 1
)

if /I "!TARGET_ARCH!"=="unknown" (
    echo [LOI] Khong tu phat hien duoc kien truc Windows.
    echo Hay chay lai tool va chon x64, x86 hoac arm64.
    pause
    exit /b 1
)

set "RID=win-!TARGET_ARCH!"
set "ARCH_STATUS=PHU HOP voi Windows hien tai"

if /I not "!TARGET_ARCH!"=="%OS_ARCH%" (
    set "ARCH_STATUS=KHAC kien truc Windows hien tai"
)

rem x86 co the chay tren Windows x64 thong thuong.
if /I "%OS_ARCH%"=="x64" if /I "!TARGET_ARCH!"=="x86" (
    set "ARCH_STATUS=x86 tren Windows x64 - thuong van chay duoc"
)

rem ------------------------------------------------------------
rem 6. Tuy chon bundle file content
rem Thuong chi can native libraries. Tuy chon nay se dua them cac
rem file content vao bundle va giai nen khi ung dung khoi dong.
rem ------------------------------------------------------------
set "INCLUDE_ALL_CONTENT=false"
echo.
set "CONTENT_CHOICE="
set /p "CONTENT_CHOICE=Gop ca file content vao EXE? [y/N]: "
if /I "!CONTENT_CHOICE!"=="Y" set "INCLUDE_ALL_CONTENT=true"
if /I "!CONTENT_CHOICE!"=="YES" set "INCLUDE_ALL_CONTENT=true"

set "OUTPUT=!PROJECT_DIR!publish\!RID!"

rem ------------------------------------------------------------
rem 7. Hien thi va yeu cau xac nhan
rem ------------------------------------------------------------
echo.
echo ============================================================
echo                    XAC NHAN DONG GOI
echo ============================================================
echo   Project        : !PROJECT_NAME!
echo   File project   : !PROJECT!
echo   Windows hien tai: %OS_ARCH%
echo   Kien truc dich : !TARGET_ARCH!
echo   RID            : !RID!
echo   Kiem tra       : !ARCH_STATUS!
echo   Self-contained : true
echo   Single file    : true
echo   Native DLL     : nhung vao EXE
echo   All content    : !INCLUDE_ALL_CONTENT!
echo   Cau hinh       : Release
echo   Thu muc output : !OUTPUT!
echo ============================================================
echo.

if /I not "!TARGET_ARCH!"=="%OS_ARCH%" (
    echo [CANH BAO] Kien truc dich khac kien truc Windows hien tai.
    echo File EXE co the khong chay truc tiep tren may dang build.
    echo.
)

set "CONFIRM="
set /p "CONFIRM=Bat dau dong goi? [y/N]: "
if /I not "!CONFIRM!"=="Y" if /I not "!CONFIRM!"=="YES" (
    echo Da huy. Khong co file nao duoc build.
    pause
    exit /b 0
)

rem ------------------------------------------------------------
rem 8. Xoa output cu va publish
rem ------------------------------------------------------------
if exist "!OUTPUT!" (
    echo.
    echo Dang xoa output cu...
    rmdir /s /q "!OUTPUT!"
    if exist "!OUTPUT!" (
        echo [LOI] Khong the xoa thu muc output cu:
        echo   !OUTPUT!
        echo Hay dong chuong trinh dang su dung file trong thu muc nay.
        pause
        exit /b 1
    )
)

echo.
echo Dang publish...
echo.

dotnet publish "!PROJECT!" ^
    -c Release ^
    -r "!RID!" ^
    --self-contained true ^
    -p:PublishSingleFile=true ^
    -p:IncludeNativeLibrariesForSelfExtract=true ^
    -p:IncludeAllContentForSelfExtract=!INCLUDE_ALL_CONTENT! ^
    -p:DebugType=None ^
    -p:DebugSymbols=false ^
    -o "!OUTPUT!"

set "BUILD_RESULT=!ERRORLEVEL!"

echo.
if not "!BUILD_RESULT!"=="0" (
    echo ============================================================
    echo [THAT BAI] dotnet publish tra ve ma loi !BUILD_RESULT!.
    echo Kiem tra noi dung loi o phia tren.
    echo ============================================================
    pause
    exit /b !BUILD_RESULT!
)

rem ------------------------------------------------------------
rem 9. Kiem tra ket qua
rem ------------------------------------------------------------
set /a EXE_COUNT=0
set "EXE_FILE="
for /f "delims=" %%E in ('dir /b /a-d "!OUTPUT!\*.exe" 2^>nul') do (
    set /a EXE_COUNT+=1
    set "EXE_FILE=!OUTPUT!\%%E"
)

echo ============================================================
echo [THANH CONG] Da dong goi xong.
echo   Thu muc: !OUTPUT!
if !EXE_COUNT! EQU 1 echo   File EXE: !EXE_FILE!
if !EXE_COUNT! GTR 1 echo   Tim thay !EXE_COUNT! file EXE trong thu muc output.
if !EXE_COUNT! EQU 0 echo   [CANH BAO] Khong tim thay file .exe trong output.
echo ============================================================
echo.
echo Cac file trong output:
dir /b "!OUTPUT!"
echo.

set "OPEN_FOLDER="
set /p "OPEN_FOLDER=Mo thu muc output? [Y/n]: "
if not defined OPEN_FOLDER set "OPEN_FOLDER=Y"
if /I "!OPEN_FOLDER!"=="Y" start "" "!OUTPUT!"
if /I "!OPEN_FOLDER!"=="YES" start "" "!OUTPUT!"

pause
exit /b 0
