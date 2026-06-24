@echo off
title SX3 Scanner - Release All
echo ===============================================
echo      SX3 Scanner - Build / Release All
echo ===============================================
echo.

cd /d "D:\Code\SX3-SCANNER-MAIN-NEW-V2\BuildTools"

if not exist "release-all.ps1" (
    echo KHONG TIM THAY FILE release-all.ps1
    echo Duong dan hien tai:
    cd
    echo.
    pause
    exit /b 1
)

powershell -NoProfile -ExecutionPolicy Bypass -File ".\release-all.ps1"

echo.
echo ===============================================
echo      DA CHAY XONG - BAM PHIM BAT KY DE DONG
echo ===============================================
pauses