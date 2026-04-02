# $safeprojectname$

This project is a NativeAOT PhantomRender sample host.

## Modes

- `Library`
  Builds an injectable internal overlay DLL. This is the default.
- `Exe`
  Builds the external DX9 sample host for quick local UI testing.

## Build The Injectable DLL

```powershell
dotnet publish "$safeprojectname$.csproj" -c Release -r win-x64 -p:PhantomRenderNativeOutputType=Library -p:SelfContained=true
```

Or use:

- `publish-x64.bat`
- `publish-x86.bat`

The published output should contain:

- `$safeprojectname$.dll`
- `cimgui.dll`
- `ImGuiImpl.dll`

## Build The External Sample EXE

```powershell
dotnet build "$safeprojectname$.csproj" -c Debug -p:PhantomRenderNativeOutputType=Exe
```

## Sample Controls

- `Insert`
  Toggle the ImGui menu.
- `Delete`
  Request overlay shutdown.
