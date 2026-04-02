@echo off
setlocal

set "SCRIPT_DIR=%~dp0"
set "OUT_DIR=%SCRIPT_DIR%artifacts\nuget"
set "VERSION=%~1"
set "ASSEMBLY_VERSION=%~2"

if "%VERSION%"=="" set "VERSION=0.1.0-local"

if "%ASSEMBLY_VERSION%"=="" (
  for /f "usebackq delims=" %%I in (`powershell -NoProfile -Command "$version = '%VERSION%'; $core = ($version -split '[-+]')[0]; if ([string]::IsNullOrWhiteSpace($core)) { throw 'Invalid package version.' }; $parts = $core.Split('.'); if ($parts.Length -lt 1 -or $parts.Length -gt 4) { throw 'Invalid package version core.' }; foreach ($part in $parts) { if ($part -notmatch '^\d+$') { throw 'Invalid package version core.' } }; while ($parts.Length -lt 4) { $parts += '0' }; [string]::Join('.', $parts[0..3])"`) do set "ASSEMBLY_VERSION=%%I"
)

if "%ASSEMBLY_VERSION%"=="" (
  echo Failed to derive assembly version from package version "%VERSION%".
  exit /b 1
)

echo Packing NuGet packages version %VERSION% with assembly version %ASSEMBLY_VERSION%
if not exist "%OUT_DIR%" mkdir "%OUT_DIR%"

dotnet pack "%SCRIPT_DIR%PhantomRender\PhantomRender.csproj" -c Release -o "%OUT_DIR%" -p:PackageVersion=%VERSION% -p:Version=%VERSION% -p:AssemblyVersion=%ASSEMBLY_VERSION% -p:FileVersion=%ASSEMBLY_VERSION% -p:InformationalVersion=%VERSION%
if errorlevel 1 exit /b %errorlevel%

dotnet pack "%SCRIPT_DIR%PhantomRender.ImGui\PhantomRender.ImGui.csproj" -c Release -o "%OUT_DIR%" -p:PackageVersion=%VERSION% -p:Version=%VERSION% -p:AssemblyVersion=%ASSEMBLY_VERSION% -p:FileVersion=%ASSEMBLY_VERSION% -p:InformationalVersion=%VERSION%
if errorlevel 1 exit /b %errorlevel%

echo.
echo Packages generated in:
echo   %OUT_DIR%
