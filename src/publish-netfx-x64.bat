@echo off
setlocal

set "SCRIPT_DIR=%~dp0"
set "PROJECT=%SCRIPT_DIR%PhantomRender.ImGui.NetFramework\PhantomRender.ImGui.NetFramework.csproj"
set "PRESET=%SCRIPT_DIR%PhantomRender.ImGui.NetFramework.json"
set "TARGET=%SCRIPT_DIR%PhantomRender.ImGui.NetFramework\bin\x64\Release\net48\PhantomRender.ImGui.NetFramework.dll"
set "DEFAULT_HYDRA=Hydra\Hydra.exe"

echo.
set /p "HYDRA_EXE=Hydra.exe path [%DEFAULT_HYDRA%]: "
if "%HYDRA_EXE%"=="" set "HYDRA_EXE=%DEFAULT_HYDRA%"

if not exist "%HYDRA_EXE%" (
    echo Hydra.exe was not found: "%HYDRA_EXE%"
    exit /b 1
)

if not exist "%PRESET%" (
    echo Preset file was not found: "%PRESET%"
    exit /b 1
)

dotnet build "%PROJECT%" -c Release -p:OutputType=Library -p:Platform=x64
if errorlevel 1 exit /b %errorlevel%

if not exist "%TARGET%" (
    echo Built DLL was not found: "%TARGET%"
    exit /b 1
)

"%HYDRA_EXE%" -file "%TARGET%" -preset-file "%PRESET%" -mode console
exit /b %errorlevel%
