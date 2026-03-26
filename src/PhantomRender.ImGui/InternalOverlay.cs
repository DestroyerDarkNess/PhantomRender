using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using PhantomRender.Core;
using PhantomRender.Core.Hooks.Graphics;
using PhantomRender.Core.Hooks.Graphics.OpenGL;
using PhantomRender.ImGui.Core;
using PhantomRender.ImGui.Core.Renderers;

namespace PhantomRender.ImGui
{
    public sealed class InternalOverlay : Overlay
    {
        private DirectX9Hook _directX9Hook;
        private OpenGLHook _openGLHook;
        private int _shutdownRequested;
        private bool _disposed;

        public InternalOverlay(GraphicsApi graphicsApi)
            : this(CreateDefaultRenderer(graphicsApi))
        {
        }

        public InternalOverlay(RendererBase renderer)
            : base(renderer)
        {
            Renderer = renderer ?? throw new ArgumentNullException(nameof(renderer));
        }

        public RendererBase Renderer { get; }

        public bool AutoLoadDependencies { get; set; } = true;

        public IntPtr DependencyModuleHandle { get; set; }

        public DX9HookFlags? DirectX9HookFlagsOverride { get; set; }

        public Func<IntPtr> WindowHandleResolver { get; set; }

        public bool IsRunning { get; private set; }

        public bool ShutdownRequested => Volatile.Read(ref _shutdownRequested) != 0;

        public bool Start()
        {
            ThrowIfDisposed();

            if (IsRunning)
            {
                return true;
            }

            if (AutoLoadDependencies && !Dependencies.LoadDependencies(DependencyModuleHandle))
            {
                return false;
            }

            bool started = Renderer.GraphicsApi switch
            {
                GraphicsApi.DirectX9 => StartDirectX9(),
                GraphicsApi.OpenGL => StartOpenGL(),
                _ => throw new NotSupportedException($"{Renderer.GraphicsApi.ToDisplayName()} does not have an internal overlay host."),
            };

            IsRunning = started;
            return started;
        }

        public bool Start(IntPtr dependencyModuleHandle)
        {
            DependencyModuleHandle = dependencyModuleHandle;
            return Start();
        }

        public void RequestShutdown()
        {
            Interlocked.Exchange(ref _shutdownRequested, 1);
        }

        public bool Initialize(nint device, nint windowHandle)
        {
            return Renderer.Initialize(device, windowHandle);
        }

        public void BeginFrame()
        {
            Renderer.NewFrame();
        }

        public void RenderFrame()
        {
            Renderer.Render();
        }

        public void OnLostDevice()
        {
            Renderer.OnLostDevice();
        }

        public void OnResetDevice()
        {
            Renderer.OnResetDevice();
        }

        public override void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            RequestShutdown();

            DisposeHooks();
            Renderer.Dispose();
            IsRunning = false;

            base.Dispose();
        }

        private bool StartDirectX9()
        {
            var renderer = Renderer as DirectX9Renderer
                ?? throw new InvalidOperationException("DirectX9 internal overlay requires a DirectX9Renderer.");

            IntPtr deviceAddress = DirectX9Hook.GetDeviceAddress();
            if (deviceAddress == IntPtr.Zero)
            {
                return false;
            }

            DX9HookFlags flags = DirectX9HookFlagsOverride ?? GetDirectX9HookFlags(renderer);
            _directX9Hook = new DirectX9Hook(deviceAddress, flags);
            _directX9Hook.OnBeforeReset += HandleDirectX9BeforeReset;
            _directX9Hook.OnAfterReset += HandleDirectX9AfterReset;

            if ((flags & DX9HookFlags.EndScene) != 0)
            {
                _directX9Hook.OnEndScene += HandleDirectX9EndScene;
            }

            if ((flags & DX9HookFlags.Present) != 0)
            {
                _directX9Hook.OnPresent += HandleDirectX9Present;
            }

            _directX9Hook.Enable();
            return true;
        }

        private bool StartOpenGL()
        {
            if (!(Renderer is OpenGLRenderer))
            {
                throw new InvalidOperationException("OpenGL internal overlay requires an OpenGLRenderer.");
            }

            _openGLHook = new OpenGLHook();
            _openGLHook.OnSwapBuffers += HandleOpenGLSwapBuffers;
            _openGLHook.Enable();
            return true;
        }

        private static DX9HookFlags GetDirectX9HookFlags(DirectX9Renderer renderer)
        {
            return renderer.InitializationEndpoint == DirectX9InitializationEndpoint.EndScene
                ? DX9HookFlags.EndScene | DX9HookFlags.Reset
                : DX9HookFlags.Present | DX9HookFlags.Reset;
        }

        private void HandleDirectX9Present(IntPtr device, IntPtr sourceRect, IntPtr destRect, IntPtr hDestWindowOverride, IntPtr dirtyRegion)
        {
            RenderDirectX9Frame(device, ResolveDirectX9WindowHandle(hDestWindowOverride));
        }

        private void HandleDirectX9EndScene(IntPtr device)
        {
            RenderDirectX9Frame(device, ResolveDirectX9WindowHandle(IntPtr.Zero));
        }

        private void RenderDirectX9Frame(IntPtr device, IntPtr windowHandle)
        {
            if (ShutdownRequested || windowHandle == IntPtr.Zero)
            {
                return;
            }

            if (!Renderer.IsInitialized && !Initialize(device, windowHandle))
            {
                return;
            }

            BeginFrame();
            RenderFrame();
        }

        private void HandleDirectX9BeforeReset(IntPtr device, PhantomRender.Core.Native.Direct3D9.D3DPRESENT_PARAMETERS presentParameters)
        {
            if (!ShutdownRequested)
            {
                OnLostDevice();
            }
        }

        private void HandleDirectX9AfterReset(IntPtr device, PhantomRender.Core.Native.Direct3D9.D3DPRESENT_PARAMETERS presentParameters)
        {
            if (!ShutdownRequested)
            {
                OnResetDevice();
            }
        }

        private void HandleOpenGLSwapBuffers(IntPtr hdc)
        {
            if (ShutdownRequested)
            {
                return;
            }

            IntPtr windowHandle = WindowFromDC(hdc);
            if (windowHandle == IntPtr.Zero)
            {
                windowHandle = ResolveWindowHandle();
            }

            if (windowHandle == IntPtr.Zero)
            {
                return;
            }

            if (!Renderer.IsInitialized && !Initialize(hdc, windowHandle))
            {
                return;
            }

            BeginFrame();
            RenderFrame();
        }

        private IntPtr ResolveDirectX9WindowHandle(IntPtr hDestWindowOverride)
        {
            if (hDestWindowOverride != IntPtr.Zero && IsWindow(hDestWindowOverride))
            {
                return hDestWindowOverride;
            }

            return ResolveWindowHandle();
        }

        private IntPtr ResolveWindowHandle()
        {
            if (WindowHandleResolver != null)
            {
                IntPtr resolved = WindowHandleResolver();
                if (resolved != IntPtr.Zero)
                {
                    return resolved;
                }
            }

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

        private void DisposeHooks()
        {
            if (_directX9Hook != null)
            {
                _directX9Hook.OnBeforeReset -= HandleDirectX9BeforeReset;
                _directX9Hook.OnAfterReset -= HandleDirectX9AfterReset;
                _directX9Hook.OnEndScene -= HandleDirectX9EndScene;
                _directX9Hook.OnPresent -= HandleDirectX9Present;

                try
                {
                    _directX9Hook.Dispose();
                }
                catch
                {
                }

                _directX9Hook = null;
            }

            if (_openGLHook != null)
            {
                _openGLHook.OnSwapBuffers -= HandleOpenGLSwapBuffers;

                try
                {
                    _openGLHook.Dispose();
                }
                catch
                {
                }

                _openGLHook = null;
            }
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(InternalOverlay));
            }
        }

        [DllImport("user32.dll")]
        private static extern IntPtr WindowFromDC(IntPtr hdc);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool IsWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();
    }
}
