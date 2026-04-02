using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using PhantomRender.Core;
using PhantomRender.ImGui;
using PhantomRender.ImGui.Core.Renderers;

namespace $rootnamespace$
{
    public static class Exports
    {
        private const uint DllProcessDetach = 0;
        private const uint DllProcessAttach = 1;

        private static readonly object SyncRoot = new object();
        private static IntPtr _moduleHandle;
        private static int _shutdownRequested;
        private static InternalOverlay _overlay;
        private static UI _ui;
        private static TextWriter _logWriter;
        private static TextWriter _originalOut;
        private static TextWriter _originalError;
        private static string _logPath;

        [UnmanagedCallersOnly(EntryPoint = "DllMain", CallConvs = new[] { typeof(CallConvStdcall) })]
        public static unsafe bool DllMain(IntPtr hModule, uint reason, IntPtr reserved)
        {
            switch (reason)
            {
                case DllProcessAttach:
                    _moduleHandle = hModule;
                    DisableThreadLibraryCalls(hModule);

                    IntPtr threadHandle = CreateThread(IntPtr.Zero, IntPtr.Zero, &InitializeThreadWrapper, IntPtr.Zero, 0, IntPtr.Zero);
                    if (threadHandle != IntPtr.Zero)
                    {
                        CloseHandle(threadHandle);
                    }
                    break;

                case DllProcessDetach:
                    RequestShutdown();
                    ShutdownInternal();
                    CloseLogging();
                    break;
            }

            return true;
        }

        [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvStdcall) })]
        private static uint InitializeThreadWrapper(IntPtr parameter)
        {
            RunInternal();
            return 0;
        }

        private static void RunInternal()
        {
            InitializeLogging();
            Log("Native bootstrap thread started.");

            try
            {
                if (!InitializeInternal())
                {
                    Log("InitializeInternal returned false.");
                    return;
                }

                Log("Overlay initialized. Entering wait loop.");

                while (!IsShutdownRequested())
                {
                    Thread.Sleep(50);
                }
            }
            catch (Exception ex)
            {
                Log($"Native bootstrap failed: {ex}");
            }
            finally
            {
                Log("Shutting down native bootstrap.");
                ShutdownInternal();
                CloseLogging();
            }
        }

        private static bool InitializeInternal()
        {
            Log("Waiting for supported graphics API...");

            GraphicsApi graphicsApi = WaitForSupportedGraphicsApi(TimeSpan.FromSeconds(15));
            if (graphicsApi == GraphicsApi.Unknown)
            {
                Log("No supported graphics API was detected within the timeout.");
                return false;
            }

            Log($"Detected graphics API: {graphicsApi.ToDisplayName()}.");

            RendererBase renderer = CreateInternalRenderer(graphicsApi);
            var overlay = new InternalOverlay(renderer)
            {
                DependencyModuleHandle = _moduleHandle,
            };
            var ui = new UI(overlay);

            if (!overlay.Start())
            {
                Log($"Overlay start failed for {graphicsApi.ToDisplayName()}.");
                ui.Dispose();
                overlay.Dispose();
                return false;
            }

            lock (SyncRoot)
            {
                _overlay = overlay;
                _ui = ui;
            }

            Log($"Overlay start succeeded for {graphicsApi.ToDisplayName()}.");
            return true;
        }

        private static GraphicsApi WaitForSupportedGraphicsApi(TimeSpan timeout)
        {
            Stopwatch stopwatch = Stopwatch.StartNew();
            while (stopwatch.Elapsed < timeout && !IsShutdownRequested())
            {
                if (GraphicsApiDetector.IsLoaded(GraphicsApi.DirectX9))
                {
                    Log($"Graphics API detection hit DX9 after {stopwatch.ElapsedMilliseconds}ms.");
                    return GraphicsApi.DirectX9;
                }

                if (GraphicsApiDetector.IsLoaded(GraphicsApi.OpenGL))
                {
                    Log($"Graphics API detection hit OpenGL after {stopwatch.ElapsedMilliseconds}ms.");
                    return GraphicsApi.OpenGL;
                }

                if (GraphicsApiDetector.IsLoaded(GraphicsApi.Vulkan))
                {
                    Log($"Graphics API detection hit Vulkan after {stopwatch.ElapsedMilliseconds}ms.");
                    return GraphicsApi.Vulkan;
                }

                if (GraphicsApiDetector.IsLoaded(GraphicsApi.DirectX12))
                {
                    Log($"Graphics API detection hit DX12 after {stopwatch.ElapsedMilliseconds}ms.");
                    return GraphicsApi.DirectX12;
                }

                if (GraphicsApiDetector.IsLoaded(GraphicsApi.DirectX11))
                {
                    Log($"Graphics API detection hit DX11 after {stopwatch.ElapsedMilliseconds}ms.");
                    return GraphicsApi.DirectX11;
                }

                if (GraphicsApiDetector.IsLoaded(GraphicsApi.DirectX10))
                {
                    Log($"Graphics API detection hit DX10 after {stopwatch.ElapsedMilliseconds}ms.");
                    return GraphicsApi.DirectX10;
                }

                Thread.Sleep(100);
            }

            return GraphicsApi.Unknown;
        }

        private static RendererBase CreateInternalRenderer(GraphicsApi graphicsApi)
        {
            switch (graphicsApi)
            {
                case GraphicsApi.DirectX12:
                    return new DirectX12Renderer();
                case GraphicsApi.DirectX11:
                    return new DirectX11Renderer();
                case GraphicsApi.DirectX10:
                    return new DirectX10Renderer();
                case GraphicsApi.DirectX9:
                    return new DirectX9Renderer();
                case GraphicsApi.OpenGL:
                    return new OpenGLRenderer();
                case GraphicsApi.Vulkan:
                    return new VulkanRenderer();
                default:
                    throw new NotSupportedException($"{graphicsApi.ToDisplayName()} does not have a NativeAOT host renderer.");
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

        private static void InitializeLogging()
        {
            try
            {
                if (_logWriter != null)
                {
                    return;
                }

                _logPath = ResolveLogPath();
                string directory = Path.GetDirectoryName(_logPath);
                if (!string.IsNullOrWhiteSpace(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                _originalOut = Console.Out;
                _originalError = Console.Error;

                var stream = new FileStream(_logPath, FileMode.Create, FileAccess.Write, FileShare.ReadWrite);
                _logWriter = TextWriter.Synchronized(new StreamWriter(stream, new UTF8Encoding(false))
                {
                    AutoFlush = true,
                });

                Console.SetOut(_logWriter);
                Console.SetError(_logWriter);
                Log($"File logging initialized at '{_logPath}'.");
            }
            catch
            {
            }
        }

        private static void CloseLogging()
        {
            try
            {
                _logWriter?.Flush();
            }
            catch
            {
            }

            try
            {
                if (_originalOut != null)
                {
                    Console.SetOut(_originalOut);
                }

                if (_originalError != null)
                {
                    Console.SetError(_originalError);
                }
            }
            catch
            {
            }

            try
            {
                _logWriter?.Dispose();
            }
            catch
            {
            }

            _logWriter = null;
            _originalOut = null;
            _originalError = null;
        }

        private static void Log(string message)
        {
            try
            {
                Console.WriteLine("[$safeprojectname$][" + DateTime.Now.ToString("HH:mm:ss.fff") + "] " + message);
            }
            catch
            {
            }
        }

        private static string ResolveLogPath()
        {
            string modulePath = GetModulePath(_moduleHandle);
            if (!string.IsNullOrWhiteSpace(modulePath))
            {
                string directory = Path.GetDirectoryName(modulePath);
                if (!string.IsNullOrWhiteSpace(directory))
                {
                    return Path.Combine(directory, "$safeprojectname$.log");
                }
            }

            return Path.Combine(Path.GetTempPath(), "$safeprojectname$", "$safeprojectname$.log");
        }

        private static string GetModulePath(IntPtr moduleHandle)
        {
            if (moduleHandle == IntPtr.Zero)
            {
                return null;
            }

            var builder = new StringBuilder(1024);
            int length = GetModuleFileNameW(moduleHandle, builder, builder.Capacity);
            return length > 0 ? builder.ToString(0, length) : null;
        }

        [DllImport("kernel32.dll")]
        private static extern unsafe IntPtr CreateThread(IntPtr threadAttributes, IntPtr stackSize, delegate* unmanaged[Stdcall]<IntPtr, uint> startAddress, IntPtr parameter, uint creationFlags, IntPtr threadId);

        [DllImport("kernel32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool DisableThreadLibraryCalls(IntPtr moduleHandle);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CloseHandle(IntPtr handle);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern int GetModuleFileNameW(IntPtr moduleHandle, StringBuilder fileName, int size);
    }
}
