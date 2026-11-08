@echo off
setlocal EnableExtensions DisableDelayedExpansion
chcp 65001 >nul
title SX3 Scanner - Build Installer and GitHub Release
pushd "%~dp0"
if errorlevel 1 exit /b 1

echo ============================================================
echo SX3 SCANNER - BUILD INSTALLER AND GITHUB RELEASE
echo Default: next minor version, commit all source changes, push,
echo          publish GitHub Release with the installer.
echo Build only: %~nx0 -BuildOnly
echo Version:    %~nx0 -Version 10.48.0
echo Draft:      %~nx0 -Version 10.48.0 -Draft
echo ============================================================
echo.

powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%~dp0BuildTools\release-all.ps1" %*
set "RELEASE_EXIT_CODE=%ERRORLEVEL%"
echo.
if "%RELEASE_EXIT_CODE%"=="0" (
    echo COMPLETED SUCCESSFULLY.
) else (
    echo FAILED. Exit code: %RELEASE_EXIT_CODE%
    echo Read the error above. GitHub Release may be partially created;
    echo do not delete or overwrite an existing release/tag automatically.
)
popd
if not defined SX3_NO_PAUSE pause
exit /b %RELEASE_EXIT_CODE%
