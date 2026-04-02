using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using PhantomRender.Core;
using PhantomRender.ImGui;
using PhantomRender.ImGui.Core.Renderers;

namespace $rootnamespace$
{
    public class dllmain
    {
        private static readonly object SyncRoot = new object();
        private static int _startupState;
        private static int _shutdownRequested;
        private static InternalOverlay _overlay;
        private static UI _ui;
        private static TextWriter _logWriter;
        private static TextWriter _originalOut;
        private static TextWriter _originalError;
        private static string _logPath;

        public static void EntryPoint()
        {
            if (Interlocked.CompareExchange(ref _startupState, 1, 0) != 0)
            {
                return;
            }

            InitializeLogging();
            Log("Managed net48 entrypoint invoked.");
            AppDomain.CurrentDomain.ProcessExit += OnProcessExit;
            AppDomain.CurrentDomain.DomainUnload += OnProcessExit;
            RunInternal();
        }

        private static void RunInternal()
        {
            Log("Managed bootstrap started.");

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
                Log($"Managed bootstrap failed: {ex}");
            }
            finally
            {
                Log("Shutting down managed bootstrap.");
                ShutdownInternal();
                CloseLogging();
                Volatile.Write(ref _startupState, 0);
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
                AutoLoadDependencies = false,
            };
            var ui = new UI(overlay);

            string assemblyDirectory = HostPathResolver.ResolveInjectedHostDirectory("$safeprojectname$.dll");
            if (!overlay.Dependencies.LoadDependencies(assemblyDirectory))
            {
                Log("Failed to load native ImGui dependencies.");
                ui.Dispose();
                overlay.Dispose();
                return false;
            }

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
                    throw new NotSupportedException($"{graphicsApi.ToDisplayName()} does not have a .NET Framework host renderer.");
            }
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

        private static void OnProcessExit(object sender, EventArgs e)
        {
            RequestShutdown();
        }

        private static void RequestShutdown()
        {
            Interlocked.Exchange(ref _shutdownRequested, 1);
            _overlay?.RequestShutdown();
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
                _logWriter = TextWriter.Synchronized(new StreamWriter(stream, new System.Text.UTF8Encoding(false))
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
            return Path.Combine(
                HostPathResolver.ResolveLoaderDirectory("$safeprojectname$.dll"),
                "$safeprojectname$.log");
        }
    }
}
