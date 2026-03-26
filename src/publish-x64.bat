@echo off
setlocal

set "PROJECT=%~dp0PhantomRender.ImGui.Native\PhantomRender.ImGui.Native.csproj"

dotnet publish "%PROJECT%" -c Release -r win-x64 -p:PhantomRenderNativeOutputType=Library -p:OutputType=Library
exit /b %errorlevel%
