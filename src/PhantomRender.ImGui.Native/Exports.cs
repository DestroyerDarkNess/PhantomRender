using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using PhantomRender.Core;
using PhantomRender.ImGui;
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
            if (graphicsApi == GraphicsApi.Unknown)
            {
                return false;
            }

            RendererBase renderer = CreateInternalRenderer(graphicsApi);
            var overlay = new InternalOverlay(renderer)
            {
                DependencyModuleHandle = _hModule,
            };

            var ui = new UI(overlay);
            if (!overlay.Start())
            {
                ui.Dispose();
                overlay.Dispose();
                return false;
            }

            lock (SyncRoot)
            {
                _overlay = overlay;
                _ui = ui;
            }

            return true;
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

        private static RendererBase CreateInternalRenderer(GraphicsApi graphicsApi)
        {
            switch (graphicsApi)
            {
                case GraphicsApi.DirectX9:
                    return new DirectX9Renderer();

                case GraphicsApi.OpenGL:
                    return new OpenGLRenderer();

                default:
                    throw new NotSupportedException($"{graphicsApi.ToDisplayName()} does not have an internal native host.");
            }
        }

        private static void RequestShutdown()
        {
            Interlocked.Exchange(ref _shutdownRequested, 1);
            _overlay?.RequestShutdown();
        }

        private static bool IsShutdownRequested()
        {
            UI ui = _ui;
            if (ui != null && ui.ShutdownRequested)
            {
                _overlay?.RequestShutdown();
            }

            return Volatile.Read(ref _shutdownRequested) != 0 ||
                   (ui != null && ui.ShutdownRequested) ||
                   (_overlay != null && _overlay.ShutdownRequested);
        }

        private static void ShutdownInternal()
        {
            UI ui = null;
            InternalOverlay overlay = null;

            lock (SyncRoot)
            {
                if (_overlay == null && _ui == null)
                {
                    return;
                }

                ui = _ui;
                overlay = _overlay;

                _ui = null;
                _overlay = null;
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
    }
}
