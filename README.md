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
  <a href="./Games.md">Games Tested Gallery</a> ·
  <a href="./KNOWN_ISSUES.md">Known Issues</a>
</p>

## Table of Contents

- [Overview](#overview)
- [Project Structure](#project-structure)
- [Features](#features)
- [Graphics Support](#graphics-support)
- [Build And Publish](#build-and-publish)
- [Debug And Test](#debug-and-test)
- [Injection Quick Start](#injection-quick-start)
- [Diagnostics](#diagnostics)
- [Known Issues](#known-issues)
- [Future Work](#future-work)
- [License](#license)

## Overview

PhantomRender is an injected ImGui overlay host for Windows titles that use:

- DirectX 9
- DirectX 10
- DirectX 11
- DirectX 12
- OpenGL
- Vulkan

The current codebase is focused on a clean internal overlay path:

- native bootstrap DLL with `DllMain` entry point
- automatic graphics API detection
- one active backend per process
- robust resize/reset handling
- minimized managed allocations in hot render paths
- file logging for injected sessions

## Project Structure

| Project | Description |
|---|---|
| `src/PhantomRender` | Core hooks and low-level graphics/input interop. |
| `src/PhantomRender.ImGui` | Overlay host and ImGui renderer layer for DX9/DX10/DX11/DX12/OpenGL/Vulkan. |
| `src/PhantomRender.ImGui.Native` | NativeAOT injected host, dependency loader, logging, and default sample UI. |

## Features

- Automatic backend probe and activation.
- DXGI-based hook path for DX10, DX11, and DX12.
- DX9 `Present` and `EndScene` support.
- OpenGL `wglSwapBuffers` hook path.
- Unity compatibility mode for DX11 and DX12 titles.
- Owner-thread pinning for render callbacks to avoid unstable cross-thread entry into the runtime.
- DXGI resize recovery and OpenGL target/context reinitialization on display changes.
- Vulkan backend bootstrap and diagnostics are present, but the backend is still under construction.
- Reduced per-frame delegate/allocation churn across render backends.
- Built-in sample UI with `Insert` toggle and `Delete` shutdown hotkeys.
- Persistent file logging for injected sessions.

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

## Debug And Test

### Debug the ImGui menu directly

To test or debug the ImGui menu itself, run the project normally in Visual Studio using the standard `Debug` configuration.

This is the fastest way to:

- verify that the sample menu opens
- test UI changes
- debug overlay logic without injection

### Build the injectable DLL

To test the injectable NativeAOT DLL, use one of the batch files in `src/`:

- `src/publish-x64.bat` for `x64` games
- `src/publish-x86.bat` for `x86` games

Each script publishes the injectable DLL and its native dependencies to the corresponding `publish` folder.

## Injection Quick Start

1. Publish the native host in `Release`.
2. Inject `PhantomRender.ImGui.Native.dll` into the target process.
3. Keep `cimgui.dll` and `ImGuiImpl.dll` next to the injected DLL.
4. Start or resume the target process.
5. Use `Insert` to show or hide the sample UI.
6. Use `Delete` to request overlay shutdown.

Notes:

- The sample internal UI starts visible in the default native host.
- Backend selection defaults to automatic detection.

## Diagnostics

On startup, the native host redirects console output to:

```text
<publish folder>/PhantomRender.Native.log
```

If that folder is not writable, it falls back to:

```text
%TEMP%\PhantomRender\PhantomRender.Native.log
```

The log includes:

- graphics API detection
- dependency load status
- hook activation
- renderer initialization
- resize/reset/context change events
- Vulkan bootstrap and unsupported-path diagnostics
- runtime errors and exceptions

### Debugging a game crash

If the game crashes after injection, use this workflow:

1. Open Visual Studio.
2. Launch the game normally.
3. Attach Visual Studio to the game process.
4. Inject `PhantomRender.ImGui.Native.dll`.
5. Wait for the crash.
6. Copy the Visual Studio error message.
7. Copy the call stack shown by Visual Studio.
8. Collect `PhantomRender.Native.log` from the same folder as the injected DLL.
9. Open an issue and include all three:
   the error message, the call stack, and the log.

### Crash Report Template

Use this template when opening an issue:

```text
Title: [Crash] <Game name> - <API> - <x86/x64>

Game:
Version:
Engine (if known):
Graphics API:
Architecture:
Injection method:

What happened:
<Short description of the crash or failure>

Expected behavior:
<What you expected instead>

Visual Studio error message:
<Paste the exact error message here>

Call stack:
<Paste the full call stack here>

PhantomRender.Native.log:
<Paste the full log here>

Extra notes:
<Anything else that may help reproduce the issue>
```

## Known Issues

See [KNOWN_ISSUES.md](./KNOWN_ISSUES.md).

## Future Work

- Expand compatibility coverage across more tested titles.
- Finish the Vulkan backend and make it usable across real titles.
- Add dedicated samples for custom overlay UIs and integrations.

## License

PhantomRender is licensed under the MIT License. See [`LICENSE`](./LICENSE).
