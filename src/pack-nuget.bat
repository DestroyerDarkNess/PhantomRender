@echo off
setlocal

set "SCRIPT_DIR=%~dp0"
set "OUT_DIR=%SCRIPT_DIR%artifacts\nuget"
set "VERSION=%~1"

if "%VERSION%"=="" set "VERSION=0.1.0-local"

echo Packing NuGet packages version %VERSION%
if not exist "%OUT_DIR%" mkdir "%OUT_DIR%"

dotnet pack "%SCRIPT_DIR%PhantomRender\PhantomRender.csproj" -c Release -o "%OUT_DIR%" -p:PackageVersion=%VERSION%
if errorlevel 1 exit /b %errorlevel%

dotnet pack "%SCRIPT_DIR%PhantomRender.ImGui\PhantomRender.ImGui.csproj" -c Release -o "%OUT_DIR%" -p:PackageVersion=%VERSION%
if errorlevel 1 exit /b %errorlevel%

echo.
echo Packages generated in:
echo   %OUT_DIR%
