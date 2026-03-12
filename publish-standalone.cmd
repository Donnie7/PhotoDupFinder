@echo off
setlocal
powershell -ExecutionPolicy Bypass -File "%~dp0publish-standalone.ps1" %*
exit /b %ERRORLEVEL%
