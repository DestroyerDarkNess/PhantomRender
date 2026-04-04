@echo off
setlocal

set "SCRIPT_DIR=%~dp0"
set "VSIX_PROJECT=%SCRIPT_DIR%PhantomRender.Templates.Vsix\PhantomRender.Templates.Vsix.csproj"
set "VSIX_OUTPUT_DIR=%SCRIPT_DIR%PhantomRender.Templates.Vsix\bin\Release"
set "VSIX_MANIFEST=%SCRIPT_DIR%PhantomRender.Templates.Vsix\source.extension.vsixmanifest"

set "PHANTOMRENDER_VERSION=%~1"
set "PHANTOMRENDER_IMGUI_VERSION=%~2"
set "VSIX_VERSION=%~3"

for /f "usebackq delims=" %%I in (`powershell -NoProfile -Command "[xml]$xml = Get-Content '%VSIX_MANIFEST%'; $ns = New-Object System.Xml.XmlNamespaceManager($xml.NameTable); $ns.AddNamespace('vsix','http://schemas.microsoft.com/developer/vsx-schema/2011'); $xml.SelectSingleNode('/vsix:PackageManifest/vsix:Metadata/vsix:Identity', $ns).Version"`) do set "CURRENT_VSIX_VERSION=%%I"

if "%PHANTOMRENDER_VERSION%"=="" (
  set /p "PHANTOMRENDER_VERSION=Enter PhantomRender package version [0.1.0-preview.2]: "
)
if "%PHANTOMRENDER_VERSION%"=="" set "PHANTOMRENDER_VERSION=0.1.0-preview.2"

if "%PHANTOMRENDER_IMGUI_VERSION%"=="" (
  set /p "PHANTOMRENDER_IMGUI_VERSION=Enter PhantomRender.ImGui package version [%PHANTOMRENDER_VERSION%]: "
)
if "%PHANTOMRENDER_IMGUI_VERSION%"=="" set "PHANTOMRENDER_IMGUI_VERSION=%PHANTOMRENDER_VERSION%"

if "%VSIX_VERSION%"=="" (
  set /p "VSIX_VERSION=Enter VSIX version [%CURRENT_VSIX_VERSION%]: "
)
if "%VSIX_VERSION%"=="" set "VSIX_VERSION=%CURRENT_VSIX_VERSION%"

echo Updating VSIX manifest version to %VSIX_VERSION%...
powershell -NoProfile -Command "[xml]$xml = Get-Content '%VSIX_MANIFEST%'; $ns = New-Object System.Xml.XmlNamespaceManager($xml.NameTable); $ns.AddNamespace('vsix','http://schemas.microsoft.com/developer/vsx-schema/2011'); $node = $xml.SelectSingleNode('/vsix:PackageManifest/vsix:Metadata/vsix:Identity', $ns); if ($null -eq $node) { throw 'VSIX Identity node not found.' }; $node.Version = '%VSIX_VERSION%'; $settings = New-Object System.Xml.XmlWriterSettings; $settings.Encoding = New-Object System.Text.UTF8Encoding($false); $settings.Indent = $true; $writer = [System.Xml.XmlWriter]::Create('%VSIX_MANIFEST%', $settings); try { $xml.Save($writer) } finally { $writer.Dispose() }"
if errorlevel 1 exit /b %errorlevel%

echo.
echo Regenerating template zip artifacts...
powershell -NoProfile -ExecutionPolicy Bypass -File "%SCRIPT_DIR%pack-templates.ps1" -PhantomRenderVersion "%PHANTOMRENDER_VERSION%" -PhantomRenderImGuiVersion "%PHANTOMRENDER_IMGUI_VERSION%"
if errorlevel 1 exit /b %errorlevel%

call :resolve_msbuild
if errorlevel 1 exit /b %errorlevel%

echo.
echo Building VSIX in Release...
"%MSBUILD_EXE%" "%VSIX_PROJECT%" /t:Rebuild /p:Configuration=Release /p:PhantomRenderTemplatePackageVersion=%PHANTOMRENDER_VERSION% /p:PhantomRenderImGuiTemplatePackageVersion=%PHANTOMRENDER_IMGUI_VERSION%
if errorlevel 1 exit /b %errorlevel%

echo.
echo VSIX build completed.
echo Output:
echo   %VSIX_OUTPUT_DIR%
echo Version:
echo   %VSIX_VERSION%
exit /b 0

:resolve_msbuild
set "MSBUILD_EXE="

for /f "delims=" %%I in ('where msbuild 2^>nul') do (
  if not defined MSBUILD_EXE set "MSBUILD_EXE=%%I"
)

if not defined MSBUILD_EXE if exist "%ProgramFiles%\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe" set "MSBUILD_EXE=%ProgramFiles%\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe"
if not defined MSBUILD_EXE if exist "%ProgramFiles%\Microsoft Visual Studio\2022\Professional\MSBuild\Current\Bin\MSBuild.exe" set "MSBUILD_EXE=%ProgramFiles%\Microsoft Visual Studio\2022\Professional\MSBuild\Current\Bin\MSBuild.exe"
if not defined MSBUILD_EXE if exist "%ProgramFiles%\Microsoft Visual Studio\2022\Enterprise\MSBuild\Current\Bin\MSBuild.exe" set "MSBUILD_EXE=%ProgramFiles%\Microsoft Visual Studio\2022\Enterprise\MSBuild\Current\Bin\MSBuild.exe"
if not defined MSBUILD_EXE if exist "%ProgramFiles(x86)%\Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin\MSBuild.exe" set "MSBUILD_EXE=%ProgramFiles(x86)%\Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin\MSBuild.exe"

if not defined MSBUILD_EXE (
  echo MSBuild.exe was not found. Open a Visual Studio Developer Command Prompt or install Visual Studio 2022 Build Tools.
  exit /b 1
)

exit /b 0
