@echo off
call "%~dp0..\Build-Installer-GitHub.cmd" %*
exit /b %ERRORLEVEL%
