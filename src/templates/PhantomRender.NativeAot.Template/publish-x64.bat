@echo off
setlocal

set "PROJECT=%~dp0$safeprojectname$.csproj"

dotnet publish "%PROJECT%" -c Release -r win-x64 -p:PhantomRenderNativeOutputType=Library -p:OutputType=Library
exit /b %errorlevel%
