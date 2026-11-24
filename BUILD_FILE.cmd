@echo off
setlocal EnableExtensions
title SX3 Scanner - Build Release x64
pushd "%~dp0"
if errorlevel 1 exit /b 1
rem Run synchronization in a separate process, then reload updated scripts.
:SYNC
powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%~dp0BuildTools\Prepare-Build.ps1"
if errorlevel 11 goto :FAILED
if errorlevel 10 (
    call "%~dp0BUILD_FILE.cmd" %*
    if errorlevel 1 exit /b 1
    exit /b 0
)
if errorlevel 1 goto :FAILED
powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%~dp0BuildTools\Publish-Release.ps1" %*
set "BUILD_EXIT=%ERRORLEVEL%"
goto :DONE
:FAILED
set "BUILD_EXIT=1"
:DONE
popd
if /I "%~1"=="-NoPause" exit /b %BUILD_EXIT%
pause
exit /b %BUILD_EXIT%
