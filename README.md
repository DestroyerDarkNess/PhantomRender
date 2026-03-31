<h1 align="center">PhantomRender</h1>
<p align="center">
  <a href="./LICENSE">
    <img src="https://img.shields.io/badge/license-MIT-green.svg?style=flat-square" alt="license"/>
  </a>
  <img src="https://img.shields.io/badge/platform-Windows-0078D7.svg?style=flat-square" alt="platform"/>
  <img src="https://img.shields.io/badge/framework-net48%20%7C%20net9.0-512BD4.svg?style=flat-square" alt="frameworks"/>
  <img src="https://img.shields.io/badge/arch-x86%20%7C%20x64-555555.svg?style=flat-square" alt="arch"/>
</p>
<p align="center">
  Universal graphics hook + ImGui injected runtime for Windows games and applications.
</p>
<p align="center">
  Includes two host variants: a modern NativeAOT host on .NET 9 and a managed host on .NET Framework 4.8.
</p>
<p align="center">
  <a href="./Games.md">Games Tested Gallery</a> ·
  <a href="./KNOWN_ISSUES.md">Known Issues</a>
</p>

## Table of Contents

- [Project Structure](#project-structure)
- [Runtime Hosts](#runtime-hosts)
- [Graphics Support](#graphics-support)
- [Build And Publish](#build-and-publish)
- [Debug And Test](#debug-and-test)
- [Injection Quick Start](#injection-quick-start)
- [Diagnostics](#diagnostics)
- [Known Issues](#known-issues)
- [Future Work](#future-work)
- [License](#license)

## Project Structure

| Project | Description |
|---|---|
| `src/PhantomRender` | Core hooks and low-level graphics/input interop. |
| `src/PhantomRender.ImGui` | Overlay host and ImGui renderer layer for DX9/DX10/DX11/DX12/OpenGL/Vulkan. |
| `src/PhantomRender.ImGui.Native` | NativeAOT injected host, dependency loader, logging, and default sample UI. |
| `src/PhantomRender.ImGui.NetFramework` | Managed .NET Framework 4.8 host with classic `Program.Main(...)` debug entrypoint and `dllmain.EntryPoint()` injection entrypoint. |

## Runtime Hosts

| Host | Runtime | Primary use |
|---|---|---|
| `PhantomRender.ImGui.Native` | .NET 9 NativeAOT | Default modern injected DLL host. |
| `PhantomRender.ImGui.NetFramework` | .NET Framework 4.8 | Managed host for classic CLR-based injection workflows. |

## Graphics Support

| API | Status | Notes |
|---|---|---|
| DirectX 9 | ✅ Supported | `Present` and `EndScene` modes are implemented. |
| DirectX 10 | ✅ Supported | DXGI `IDXGISwapChain::Present` path. |
| DirectX 11 | ✅ Supported | Stable resize path, owner-thread filtering, and Unity compatibility mode. |
| DirectX 12 | ✅ Supported | Queue capture path with Unity compatibility mode; still validate per title. |
| OpenGL | ✅ Supported | `wglSwapBuffers` hook path with target/context reinit on change. |
| Vulkan | 🚧 In Progress | Backend is still under construction and is not considered working yet. |

## Build And Publish

### Requirements

- Windows
- .NET 9 SDK
- Visual Studio 2022 or compatible MSVC build tools for NativeAOT publish

### NativeAOT host (.NET 9)

### Debug build

```powershell
dotnet build src/PhantomRender.ImGui.Native/PhantomRender.ImGui.Native.csproj -c Debug -p:AutoPublishOnBuild=false
```

### Release publish

```powershell
dotnet publish src/PhantomRender.ImGui.Native/PhantomRender.ImGui.Native.csproj -c Release -r win-x64 -p:SkipAutoPublish=true
```

### Published output

The default injected payload is produced at:

```text
src/PhantomRender.ImGui.Native/bin/Release/net9.0/win-x64/publish/
```

That folder contains:

- `PhantomRender.ImGui.Native.dll`
- `cimgui.dll`
- `ImGuiImpl.dll`

### Managed host (.NET Framework 4.8)

```powershell
dotnet build src/PhantomRender.ImGui.NetFramework/PhantomRender.ImGui.NetFramework.csproj -c Debug
```

This host is the .NET Framework 4.8 equivalent of `PhantomRender.ImGui.Native`.

- `Program.Main(...)` is the classic console/debug entrypoint.
- `dllmain.EntryPoint()` is the managed injection entrypoint.
- Standard build output goes to the project's normal `bin\<Configuration>\net48\` folder.

## Debug And Test

### Debug the ImGui menu directly

To test or debug the ImGui menu itself, run `PhantomRender.ImGui.Native` as a normal console app in Visual Studio using the standard `Debug` configuration.

This is the fastest way to:

- verify that the sample menu opens
- test UI changes
- debug overlay logic without injection

If you want the classic managed path instead, run `PhantomRender.ImGui.NetFramework` as a console app and use its `Program.Main(...)` entrypoint.

### Build the injectable DLL

To test the injectable NativeAOT DLL, use one of the batch files in `src/`:

- `src/publish-x64.bat` for `x64` games
- `src/publish-x86.bat` for `x86` games

Each script publishes the injectable DLL and its native dependencies to the corresponding `publish` folder.

## Injection Quick Start

1. Inject `PhantomRender.ImGui.Native.dll` into the target game process.
2. Use `Insert` to show or hide the sample UI.
3. Use `Delete` to request overlay shutdown.

## Diagnostics

On startup, the native host redirects console output to:

```text
<publish folder>/PhantomRender.Native.log
```

### Debugging a game crash

If the game crashes after injection, use this workflow:

1. Open Visual Studio.
2. Launch the game normally.
3. In Visual Studio, open `Debug > Attach to Process...`.
4. Find the game process in the list and select it.
5. Press `Attach`.
6. Inject `PhantomRender.ImGui.Native.dll`.
7. Wait for the crash.
8. Copy the Visual Studio error message.
9. Open the `Call Stack` window and copy the call stack shown by Visual Studio.
10. Collect `PhantomRender.Native.log` from the same folder as the injected DLL.
11. Open an issue and include all three:
   the error message, the call stack, and the log.

## Known Issues

See [KNOWN_ISSUES.md](./KNOWN_ISSUES.md).

## Future Work

- Expand compatibility coverage across more tested titles.
- Finish the Vulkan backend and make it usable across real titles.
- Add dedicated samples for custom overlay UIs and integrations.

## License

PhantomRender is licensed under the MIT License. See [`LICENSE`](./LICENSE).
