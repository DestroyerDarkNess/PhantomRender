# Known Issues

This file tracks the remaining non-blocking issues for the current release.

## Current Items

### 1. `MinHook.NET` emits a NativeAOT publish warning

`dotnet publish` for `PhantomRender.ImGui.Native` currently reports:

```text
warning IL3053: Assembly 'MinHook.NET' produced AOT analysis warnings.
```

Current status:

- The published DLL works in current testing.
- This is a warning from the dependency, not a known runtime failure in the current codebase.

### 2. DX10 build emits one compiler warning

`DirectX10Hook.cs` currently reports:

```text
CS9191: The 'ref' modifier for argument 2 corresponding to 'in' parameter is equivalent to 'in'.
```

Current status:

- This is cosmetic.
- It does not block build, publish, or current runtime behavior.

### 3. Vulkan late injection and split-queue setups are not fully universal

The Vulkan backend is implemented, but two structural cases are still weaker than the DX/OpenGL paths:

- If the DLL is injected after the game already created and cached its Vulkan queue and swapchain, the hook may miss enough lifecycle state to initialize until the swapchain is recreated.
- The current renderer expects the presenting queue family to also support graphics. Engines that present from a separate non-graphics queue are not fully covered yet.

Current status:

- The log now emits explicit Vulkan diagnostics for these cases.
- Standard single-queue or graphics-capable present-queue paths are supported.

### 4. Compatibility is validated per tested title, not universally guaranteed

PhantomRender is stable in the current tested set, but injected graphics hooks are still sensitive to engine-specific behavior.

Current status:

- DX9, DX10, DX11, DX12, OpenGL, and Vulkan are implemented.
- Additional titles may still expose new edge cases in swapchain, reset, resize, or context lifecycle behavior.

Reference:

- [Games.md](./Games.md)
