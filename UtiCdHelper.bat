@echo off
setlocal

rem Launcher for the portable build.
rem Looks for the executable in "Portable" beside this file, and in
rem "bin\publish\Portable" when it is run from the repository root.

set "TARGET=%~dp0Portable\UtiCdHelper.exe"
if not exist "%TARGET%" set "TARGET=%~dp0bin\publish\Portable\UtiCdHelper.exe"

if not exist "%TARGET%" (
    echo Not found: "%TARGET%"
    echo Keep this .bat beside the "Portable" folder, or run
    echo "dotnet publish -p:PublishProfile=Portable" first.
    pause
    exit /b 1
)

start "" "%TARGET%"
