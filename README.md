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
  Universal graphics hook + ImGui injected runtime for Windows games (DX9/DXGI/OpenGL).
</p>

[![-----------------------------------------------------](https://raw.githubusercontent.com/andreasbm/readme/master/assets/lines/colored.png)](#table-of-contents)

## Table of Contents

- [Overview](#overview)
- [Project Structure](#project-structure)
- [Features](#features)
- [Graphics Support](#graphics-support)
- [Build](#build)
- [Injection Quick Start](#injection-quick-start)
- [Diagnostics](#diagnostics)
- [Roadmap](#roadmap)
- [License](#license)

[![-----------------------------------------------------](https://raw.githubusercontent.com/andreasbm/readme/master/assets/lines/colored.png)](#table-of-contents)

## Overview

PhantomRender is the successor of RenderSpy focused on modern .NET and injected overlays.

It provides:
- Graphics hook backends (DXGI, DX9, OpenGL).
- ImGui renderers for DirectX 9/10/11/12 and OpenGL.
- NativeAOT bootstrap DLL (`PhantomRender.ImGui.Native.dll`) with `DllMain` export.
- Automatic backend probing and activation.
- Per-process log output for debugging injected sessions.

[![-----------------------------------------------------](https://raw.githubusercontent.com/andreasbm/readme/master/assets/lines/colored.png)](#table-of-contents)

## Project Structure

| Project | Description |
|---|---|
| `src/PhantomRender` | Core hooks and low-level graphics/input interop (`MinHook.NET`, DX9/DXGI/OpenGL hook classes). |
| `src/PhantomRender.ImGui` | Overlay manager and ImGui renderer layer (DX9/DX10/DX11/DX12/OpenGL renderers). |
| `src/PhantomRender.ImGui.Native` | NativeAOT injected host, dependency loader, crash/log diagnostics, default overlay bootstrap/UI. |

[![-----------------------------------------------------](https://raw.githubusercontent.com/andreasbm/readme/master/assets/lines/colored.png)](#table-of-contents)

## Features

- Auto backend probe order with single-active-backend lock.
- DXGI path with runtime API detection (DX10 / DX11 / DX12 device query).
- DX9 `Present` and `EndScene` modes.
- OpenGL `wglSwapBuffers` hook path.
- ImGui context bootstrapping + backend binding (Win32 + graphics backend).
- Runtime crash/log instrumentation for injected processes.
- Default overlay UI (hidden by default, toggle with `Insert`).

[![-----------------------------------------------------](https://raw.githubusercontent.com/andreasbm/readme/master/assets/lines/colored.png)](#table-of-contents)

## Graphics Support

| API | Status | Notes |
|---|---|---|
| DirectX 9 | Supported | `Present` and `EndScene` hook modes. |
| DirectX 10 | Supported | Through DXGI `IDXGISwapChain::Present`. |
| DirectX 11 | Supported | Primary tested path for modern desktop titles. |
| DirectX 12 | Experimental | Renderer exists under `NET5_0_OR_GREATER`; title-specific validation required. |
| OpenGL | Supported | `wglSwapBuffers` hook path. |
| Vulkan | Not implemented | Planned roadmap item. |

[![-----------------------------------------------------](https://raw.githubusercontent.com/andreasbm/readme/master/assets/lines/colored.png)](#table-of-contents)

## Build

### Requirements

- Windows
- Visual Studio 2022 (or compatible Build Tools)
- .NET 9 SDK

### Build command

```powershell
dotnet build src/PhantomRender.sln -c Release -p:Platform=x64
```

### Native output (default x64)

After build/publish, native runtime artifacts are copied to:

```text
src/PhantomRender.ImGui.Native/bin/x64/Release/net9.0/win-x64/native/
```

This folder contains:
- `PhantomRender.ImGui.Native.dll`
- `cimgui.dll`
- `ImGuiImpl.dll`

[![-----------------------------------------------------](https://raw.githubusercontent.com/andreasbm/readme/master/assets/lines/colored.png)](#table-of-contents)

## Injection Quick Start

1. Build in `Release|x64`.
2. Inject `PhantomRender.ImGui.Native.dll` into the target process.
3. Ensure `cimgui.dll` and `ImGuiImpl.dll` are in the same folder as the injected DLL.
4. Start the game/application.
5. Press `Insert` to toggle the default overlay UI.

Notes:
- Overlay starts hidden by design.
- Hook selection defaults to `Auto` (`OverlayHookKind.Auto`).

[![-----------------------------------------------------](https://raw.githubusercontent.com/andreasbm/readme/master/assets/lines/colored.png)](#table-of-contents)

## Diagnostics

On injection, PhantomRender mirrors console output into a per-process file log:

```text
<native output folder>/<process-name>.log
```

Example:

```text
.../native/witcher3.log
```

Log includes:
- session header with timestamp and PID
- dependency load results (`cimgui.dll`, `ImGuiImpl.dll`)
- hook activation path
- renderer initialization trace
- runtime errors/exceptions

[![-----------------------------------------------------](https://raw.githubusercontent.com/andreasbm/readme/master/assets/lines/colored.png)](#table-of-contents)

## Roadmap

- Improve DX11 stability in long sessions and edge swapchain scenarios.
- Expand DX12 validation across more engines/titles.
- Add Vulkan hook/render path.
- Add samples repo for custom UI and plugin-style integrations.

[![-----------------------------------------------------](https://raw.githubusercontent.com/andreasbm/readme/master/assets/lines/colored.png)](#table-of-contents)

## License

PhantomRender is licensed under the MIT License. See [`LICENSE`](./LICENSE).
