@echo off
setlocal

set "REPO_ROOT=%~dp0"
pushd "%REPO_ROOT%" >nul

if "%~1"=="" (
  dotnet run --project "%REPO_ROOT%src\PhotoDupFinder.Cli" -- start
) else (
  dotnet run --project "%REPO_ROOT%src\PhotoDupFinder.Cli" -- %*
)

set "EXIT_CODE=%ERRORLEVEL%"
popd >nul
exit /b %EXIT_CODE%
