@echo off
cd /d D:\Nam\SX3-SCANNER-MAIN

powershell -ExecutionPolicy Bypass -File .\BuildTools\Publish-Announcement.ps1 ^
  -ServerUrl "http://100.72.125.42:5088" ^
  -Token "SX3-2026-Admin-Token-Nam0616"

pause