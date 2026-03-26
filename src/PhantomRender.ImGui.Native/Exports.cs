using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using PhantomRender.Core;
using PhantomRender.Core.Hooks.Graphics;
using PhantomRender.Core.Hooks.Graphics.OpenGL;
using PhantomRender.ImGui;
using PhantomRender.ImGui.Core;
using PhantomRender.ImGui.Core.Renderers;

namespace PhantomRender.ImGui.Native
{
    public static class Exports
    {
        private const uint DLL_PROCESS_DETACH = 0;
        private const uint DLL_PROCESS_ATTACH = 1;

        private static readonly object SyncRoot = new object();
        private static IntPtr _hModule;
        private static int _shutdownRequested;
        private static InternalOverlay _overlay;
        private static UI _ui;
        private static DirectX9Hook _directX9Hook;
        private static OpenGLHook _openGLHook;

        [UnmanagedCallersOnly(EntryPoint = "DllMain", CallConvs = new[] { typeof(CallConvStdcall) })]
        public static unsafe bool DllMain(IntPtr hModule, uint ul_reason_for_call, IntPtr lpReserved)
        {
            switch (ul_reason_for_call)
            {
                case DLL_PROCESS_ATTACH:
                    _hModule = hModule;
                    DisableThreadLibraryCalls(hModule);

                    IntPtr handle = CreateThread(IntPtr.Zero, IntPtr.Zero, &InitializeThreadWrapper, IntPtr.Zero, 0, IntPtr.Zero);
                    if (handle != IntPtr.Zero)
                    {
                        CloseHandle(handle);
                    }
                    break;

                case DLL_PROCESS_DETACH:
                    RequestShutdown();
                    ShutdownInternal();
                    break;
            }

            return true;
        }

        [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvStdcall) })]
        private static uint InitializeThreadWrapper(IntPtr lpParam)
        {
            RunInternal();
            return 0;
        }

        private static void RunInternal()
        {
            try
            {
                if (!InitializeInternal())
                {
                    return;
                }

                while (!IsShutdownRequested())
                {
                    Thread.Sleep(50);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[PhantomRender] Internal bootstrap failed: {ex}");
            }
            finally
            {
                ShutdownInternal();
            }
        }

        private static bool InitializeInternal()
        {
            GraphicsApi graphicsApi = WaitForSupportedGraphicsApi(TimeSpan.FromSeconds(15));
            return graphicsApi switch
            {
                GraphicsApi.DirectX9 => InitializeDirectX9(),
                GraphicsApi.OpenGL => InitializeOpenGL(),
                _ => false,
            };

            var resolver = new DependencyResolver();
            if (!resolver.LoadDependencies(_hModule))
            {
                Console.WriteLine("[PhantomRender] Failed to load native dependencies.");
                return false;
            }
        }

        private static GraphicsApi WaitForSupportedGraphicsApi(TimeSpan timeout)
        {
            Stopwatch stopwatch = Stopwatch.StartNew();
            while (stopwatch.Elapsed < timeout && !IsShutdownRequested())
            {
                if (GraphicsApiDetector.IsLoaded(GraphicsApi.DirectX9))
                {
                    return GraphicsApi.DirectX9;
                }

                if (GraphicsApiDetector.IsLoaded(GraphicsApi.OpenGL))
                {
                    return GraphicsApi.OpenGL;
                }

                Thread.Sleep(100);
            }

            return GraphicsApi.Unknown;
        }

        private static bool InitializeDirectX9()
        {
            var renderer = new DirectX9Renderer();
            var overlay = new InternalOverlay(renderer);
            var ui = new UI(overlay);

            IntPtr deviceAddress = DirectX9Hook.GetDeviceAddress();
            if (deviceAddress == IntPtr.Zero)
            {
                ui.Dispose();
                overlay.Dispose();
                return false;
            }

            DX9HookFlags flags = renderer.InitializationEndpoint == DirectX9InitializationEndpoint.EndScene
                ? DX9HookFlags.EndScene | DX9HookFlags.Reset
                : DX9HookFlags.Present | DX9HookFlags.Reset;

            var directX9Hook = new DirectX9Hook(deviceAddress, flags);
            directX9Hook.OnBeforeReset += HandleDirectX9BeforeReset;
            directX9Hook.OnAfterReset += HandleDirectX9AfterReset;

            if (renderer.InitializationEndpoint == DirectX9InitializationEndpoint.EndScene)
            {
                directX9Hook.OnEndScene += HandleDirectX9EndScene;
            }
            else
            {
                directX9Hook.OnPresent += HandleDirectX9Present;
            }

            lock (SyncRoot)
            {
                _overlay = overlay;
                _ui = ui;
                _directX9Hook = directX9Hook;
            }

            directX9Hook.Enable();
            return true;
        }

        private static bool InitializeOpenGL()
        {
            var overlay = new InternalOverlay(new OpenGLRenderer());
            var ui = new UI(overlay);
            var openGLHook = new OpenGLHook();
            openGLHook.OnSwapBuffers += HandleOpenGLSwapBuffers;

            lock (SyncRoot)
            {
                _overlay = overlay;
                _ui = ui;
                _openGLHook = openGLHook;
            }

            openGLHook.Enable();
            return true;
        }

        private static void HandleDirectX9Present(IntPtr device, IntPtr sourceRect, IntPtr destRect, IntPtr hDestWindowOverride, IntPtr dirtyRegion)
        {
            RenderDirectX9Frame(device, ResolveDirectX9WindowHandle(hDestWindowOverride));
        }

        private static void HandleDirectX9EndScene(IntPtr device)
        {
            RenderDirectX9Frame(device, ResolveDirectX9WindowHandle(IntPtr.Zero));
        }

        private static void RenderDirectX9Frame(IntPtr device, IntPtr windowHandle)
        {
            InternalOverlay overlay = _overlay;
            if (overlay == null || windowHandle == IntPtr.Zero)
            {
                return;
            }

            if (!overlay.Renderer.IsInitialized && !overlay.Initialize(device, windowHandle))
            {
                return;
            }

            overlay.BeginFrame();
            overlay.RenderFrame();

            if (_ui != null && _ui.ShutdownRequested)
            {
                RequestShutdown();
            }
        }

        private static void HandleDirectX9BeforeReset(IntPtr device, PhantomRender.Core.Native.Direct3D9.D3DPRESENT_PARAMETERS presentParameters)
        {
            _overlay?.OnLostDevice();
        }

        private static void HandleDirectX9AfterReset(IntPtr device, PhantomRender.Core.Native.Direct3D9.D3DPRESENT_PARAMETERS presentParameters)
        {
            _overlay?.OnResetDevice();
        }

        private static void HandleOpenGLSwapBuffers(IntPtr hdc)
        {
            InternalOverlay overlay = _overlay;
            if (overlay == null)
            {
                return;
            }

            IntPtr windowHandle = WindowFromDC(hdc);
            if (windowHandle == IntPtr.Zero)
            {
                windowHandle = ResolveMainWindowHandle();
            }

            if (windowHandle == IntPtr.Zero)
            {
                return;
            }

            if (!overlay.Renderer.IsInitialized && !overlay.Initialize(hdc, windowHandle))
            {
                return;
            }

            overlay.BeginFrame();
            overlay.RenderFrame();

            if (_ui != null && _ui.ShutdownRequested)
            {
                RequestShutdown();
            }
        }

        private static IntPtr ResolveDirectX9WindowHandle(IntPtr hDestWindowOverride)
        {
            if (hDestWindowOverride != IntPtr.Zero && IsWindow(hDestWindowOverride))
            {
                return hDestWindowOverride;
            }

            return ResolveMainWindowHandle();
        }

        private static IntPtr ResolveMainWindowHandle()
        {
            try
            {
                IntPtr mainWindow = Process.GetCurrentProcess().MainWindowHandle;
                if (mainWindow != IntPtr.Zero && IsWindow(mainWindow))
                {
                    return mainWindow;
                }
            }
            catch
            {
            }

            IntPtr foregroundWindow = GetForegroundWindow();
            if (foregroundWindow != IntPtr.Zero && IsWindow(foregroundWindow))
            {
                return foregroundWindow;
            }

            return IntPtr.Zero;
        }

        private static void RequestShutdown()
        {
            Interlocked.Exchange(ref _shutdownRequested, 1);
        }

        private static bool IsShutdownRequested()
        {
            return Volatile.Read(ref _shutdownRequested) != 0 || (_ui != null && _ui.ShutdownRequested);
        }

        private static void ShutdownInternal()
        {
            DirectX9Hook directX9Hook = null;
            OpenGLHook openGLHook = null;
            UI ui = null;
            InternalOverlay overlay = null;

            lock (SyncRoot)
            {
                if (_overlay == null && _ui == null && _directX9Hook == null && _openGLHook == null)
                {
                    return;
                }

                directX9Hook = _directX9Hook;
                openGLHook = _openGLHook;
                ui = _ui;
                overlay = _overlay;

                _directX9Hook = null;
                _openGLHook = null;
                _ui = null;
                _overlay = null;
            }

            try
            {
                directX9Hook?.Dispose();
            }
            catch
            {
            }

            try
            {
                openGLHook?.Dispose();
            }
            catch
            {
            }

            try
            {
                ui?.Dispose();
            }
            catch
            {
            }

            try
            {
                overlay?.Dispose();
            }
            catch
            {
            }
        }

        [DllImport("kernel32.dll")]
        private static extern unsafe IntPtr CreateThread(IntPtr lpThreadAttributes, IntPtr dwStackSize, delegate* unmanaged[Stdcall]<IntPtr, uint> lpStartAddress, IntPtr lpParameter, uint dwCreationFlags, IntPtr lpThreadId);

        [DllImport("kernel32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool DisableThreadLibraryCalls(IntPtr hModule);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CloseHandle(IntPtr hObject);

        [DllImport("user32.dll")]
        private static extern IntPtr WindowFromDC(IntPtr hdc);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool IsWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();
    }
}