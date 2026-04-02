# $safeprojectname$

This project is a .NET Framework 4.8 PhantomRender sample host.

## Modes

- `Library`
  Builds the injectable managed host. This is the default.
- `Exe`
  Builds the external DX9 sample host for quick local UI testing.

## Build The Injectable DLL

```powershell
dotnet build "$safeprojectname$.csproj" -c Release -p:PhantomRenderNetFrameworkOutputType=Library -p:Platform=x64
```

Or use:

- `publish-netfx-x64.bat`
- `publish-netfx-x86.bat`

The build output should contain:

- `$safeprojectname$.dll`
- `cimgui.dll`
- `ImGuiImpl.dll`
- `$safeprojectname$.json`

## Hydra Preset

The template includes a ready-to-use Hydra preset:

- `$safeprojectname$.json`

It is already configured to target:

- assembly: `$safeprojectname$.dll`
- entry point type: `$rootnamespace$.dllmain`
- entry point method: `EntryPoint`

## Build The External Sample EXE

```powershell
dotnet build "$safeprojectname$.csproj" -c Debug -p:PhantomRenderNetFrameworkOutputType=Exe
```

## Sample Controls

- `Insert`
  Toggle the ImGui menu.
- `Delete`
  Request overlay shutdown.
