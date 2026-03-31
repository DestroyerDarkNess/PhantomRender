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

### 3. Vulkan backend is still under construction

The Vulkan path is not considered working yet.

Current status:

- Bootstrap and diagnostics exist.
- The backend is still incomplete and should be treated as work in progress, not as supported runtime functionality.

### 4. Compatibility is validated per tested title, not universally guaranteed

PhantomRender is stable in the current tested set, but injected graphics hooks are still sensitive to engine-specific behavior.

Current status:

- DX9, DX10, DX11, DX12, and OpenGL are implemented.
- Unity-specific compatibility handling exists for DX11 and DX12.
- Additional titles may still expose new edge cases in swapchain, reset, resize, or context lifecycle behavior.

Reference:

- [Games.md](./Games.md)
