@echo off
setlocal
powershell -ExecutionPolicy Bypass -File "%~dp0install-tool.ps1" %*
exit /b %ERRORLEVEL%
