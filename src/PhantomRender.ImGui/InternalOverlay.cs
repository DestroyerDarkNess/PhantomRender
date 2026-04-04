using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using PhantomRender.Core;
using PhantomRender.Core.Hooks.Graphics;
using PhantomRender.Core.Hooks.Graphics.OpenGL;
using PhantomRender.Core.Hooks.Graphics.Vulkan;
using PhantomRender.ImGui.Core;
using PhantomRender.ImGui.Core.Renderers;

namespace PhantomRender.ImGui
{
    public sealed class InternalOverlay : Overlay
    {
        private DirectX9Hook _directX9Hook;
        private DirectX10Hook _directX10Hook;
        private DirectX11Hook _directX11Hook;
        private DirectX12Hook _directX12Hook;
        private OpenGLHook _openGLHook;
        private VulkanHook _vulkanHook;
        private IntPtr _directX9DeviceHandle;
        private IntPtr _directX9WindowHandle;
        private int _directX9PresentThreadId;
        private bool _directX9LoggedThreadMismatch;
        private IntPtr _openGLDeviceContext;
        private IntPtr _openGLRenderingContext;
        private IntPtr _openGLWindowHandle;
        private int _openGLPresentThreadId;
        private bool _openGLLoggedThreadMismatch;
        private IntPtr _directX10SwapChainHandle;
        private IntPtr _directX10WindowHandle;
        private int _directX10PresentThreadId;
        private bool _directX10LoggedThreadMismatch;
        private IntPtr _directX11SwapChainHandle;
        private IntPtr _directX11WindowHandle;
        private int _directX11PresentThreadId;
        private bool _directX11LoggedThreadMismatch;
        private IntPtr _directX12SwapChainHandle;
        private IntPtr _directX12WindowHandle;
        private int _directX12PresentThreadId;
        private bool _directX12LoggedThreadMismatch;
        private IntPtr _vulkanDeviceHandle;
        private IntPtr _vulkanSwapchainHandle;
        private IntPtr _vulkanWindowHandle;
        private int _vulkanPresentThreadId;
        private bool _vulkanLoggedThreadMismatch;
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

        public bool EnableDirectX11ResizeBuffersHook { get; set; } = true;

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
                GraphicsApi.DirectX10 => StartDirectX10(),
                GraphicsApi.DirectX11 => StartDirectX11(),
                GraphicsApi.DirectX12 => StartDirectX12(),
                GraphicsApi.OpenGL => StartOpenGL(),
                GraphicsApi.Vulkan => StartVulkan(),
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

            IntPtr deviceAddress = DirectX9Hook.GetDeviceAddress(out bool isExDevice);
            if (deviceAddress == IntPtr.Zero)
            {
                return false;
            }

            DX9HookFlags flags = DirectX9HookFlagsOverride ?? GetDirectX9HookFlags(renderer);
            _directX9Hook = new DirectX9Hook(deviceAddress, flags, isExDevice);
            _directX9DeviceHandle = IntPtr.Zero;
            _directX9WindowHandle = IntPtr.Zero;
            _directX9PresentThreadId = 0;
            _directX9LoggedThreadMismatch = false;
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
            if (!(Renderer is OpenGLRenderer renderer))
            {
                throw new InvalidOperationException("OpenGL internal overlay requires an OpenGLRenderer.");
            }

            _openGLHook = new OpenGLHook(renderer.SwapBuffersHookTarget);
            _openGLDeviceContext = IntPtr.Zero;
            _openGLRenderingContext = IntPtr.Zero;
            _openGLWindowHandle = IntPtr.Zero;
            _openGLPresentThreadId = 0;
            _openGLLoggedThreadMismatch = false;
            _openGLHook.OnSwapBuffers += HandleOpenGLSwapBuffers;
            _openGLHook.Enable();
            return true;
        }

        private bool StartDirectX10()
        {
            ValidateDxgiRenderer(GraphicsApi.DirectX10);

            IntPtr swapChainAddress = DirectX10Hook.GetSwapChainAddress();
            if (swapChainAddress == IntPtr.Zero)
            {
                return false;
            }

            _directX10Hook = new DirectX10Hook(swapChainAddress);
            _directX10SwapChainHandle = IntPtr.Zero;
            _directX10WindowHandle = IntPtr.Zero;
            _directX10PresentThreadId = 0;
            _directX10LoggedThreadMismatch = false;
            _directX10Hook.OnPresent += HandleDirectX10Present;
            _directX10Hook.OnBeforeResizeBuffers += HandleDirectX10BeforeResizeBuffers;
            _directX10Hook.OnAfterResizeBuffers += HandleDirectX10AfterResizeBuffers;
            _directX10Hook.Enable();
            return true;
        }

        private bool StartDirectX11()
        {
            ValidateDxgiRenderer(GraphicsApi.DirectX11);

            _directX11Hook = DirectX11Hook.Create(EnableDirectX11ResizeBuffersHook);
            if (_directX11Hook == null)
            {
                return false;
            }

            _directX11SwapChainHandle = IntPtr.Zero;
            _directX11WindowHandle = IntPtr.Zero;
            _directX11PresentThreadId = 0;
            _directX11LoggedThreadMismatch = false;
            _directX11Hook.OnPresent += HandleDirectX11Present;

            if (EnableDirectX11ResizeBuffersHook)
            {
                _directX11Hook.OnBeforeResizeBuffers += HandleDirectX11BeforeResizeBuffers;
                _directX11Hook.OnAfterResizeBuffers += HandleDirectX11AfterResizeBuffers;
            }

            _directX11Hook.Enable();
            return true;
        }

        private bool StartDirectX12()
        {
            ValidateDxgiRenderer(GraphicsApi.DirectX12);

            IntPtr swapChainAddress = DirectX12Hook.GetSwapChainAddress();
            if (swapChainAddress == IntPtr.Zero)
            {
                return false;
            }

            _directX12Hook = new DirectX12Hook(swapChainAddress);
            _directX12PresentThreadId = 0;
            _directX12LoggedThreadMismatch = false;
            _directX12Hook.OnPresent += HandleDirectX12Present;
            _directX12Hook.OnResizeBuffers += HandleDirectX12ResizeBuffers;
            _directX12Hook.Enable();
            return true;
        }

        private bool StartVulkan()
        {
            if (!(Renderer is IVulkanOverlayRenderer))
            {
                throw new InvalidOperationException("Vulkan internal overlay requires a Vulkan renderer.");
            }

            _vulkanHook = new VulkanHook();
            _vulkanDeviceHandle = IntPtr.Zero;
            _vulkanSwapchainHandle = IntPtr.Zero;
            _vulkanWindowHandle = IntPtr.Zero;
            _vulkanPresentThreadId = 0;
            _vulkanLoggedThreadMismatch = false;
            _vulkanHook.OnPresent += HandleVulkanPresent;
            _vulkanHook.Enable();
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

            if (!TryAcceptDirectX9Device(device, windowHandle))
            {
                return;
            }

            if (_directX9WindowHandle != IntPtr.Zero)
            {
                windowHandle = _directX9WindowHandle;
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
            if (_directX9DeviceHandle != IntPtr.Zero && device != _directX9DeviceHandle)
            {
                return;
            }

            if (!ShutdownRequested)
            {
                OnLostDevice();
            }
        }

        private void HandleDirectX9AfterReset(IntPtr device, PhantomRender.Core.Native.Direct3D9.D3DPRESENT_PARAMETERS presentParameters)
        {
            if (_directX9DeviceHandle != IntPtr.Zero && device != _directX9DeviceHandle)
            {
                return;
            }

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

            IntPtr renderingContext = wglGetCurrentContext();
            IntPtr currentDeviceContext = wglGetCurrentDC();
            if (renderingContext == IntPtr.Zero || currentDeviceContext == IntPtr.Zero)
            {
                return;
            }

            IntPtr targetDeviceContext = hdc != IntPtr.Zero ? hdc : currentDeviceContext;
            if (targetDeviceContext == IntPtr.Zero)
            {
                targetDeviceContext = currentDeviceContext;
            }

            IntPtr windowHandle = WindowFromDC(targetDeviceContext);
            if (windowHandle == IntPtr.Zero && currentDeviceContext != targetDeviceContext)
            {
                windowHandle = WindowFromDC(currentDeviceContext);
            }
            if (windowHandle == IntPtr.Zero)
            {
                windowHandle = ResolveWindowHandle();
            }

            if (windowHandle == IntPtr.Zero)
            {
                return;
            }

            if (!TryAcceptOpenGLTarget(currentDeviceContext, windowHandle, renderingContext))
            {
                return;
            }

            targetDeviceContext = _openGLDeviceContext != IntPtr.Zero ? _openGLDeviceContext : currentDeviceContext;
            if (_openGLWindowHandle != IntPtr.Zero)
            {
                windowHandle = _openGLWindowHandle;
            }

            if (!TryRenderOpenGLFrame(targetDeviceContext, renderingContext, windowHandle))
            {
                return;
            }
        }

        private unsafe void HandleVulkanPresent(ref VulkanPresentHookArgs hookArgs)
        {
            if (ShutdownRequested ||
                _vulkanHook == null ||
                !(Renderer is IVulkanOverlayRenderer vulkanRenderer))
            {
                return;
            }

            if (!_vulkanHook.TryGetPresentContext(hookArgs.Queue, hookArgs.PresentInfo, _vulkanSwapchainHandle, out VulkanPresentContext context))
            {
                return;
            }

            if (context.WindowHandle == IntPtr.Zero)
            {
                context.WindowHandle = ResolveWindowHandle();
            }

            if (!TryAcceptVulkanPresentTarget(context))
            {
                return;
            }

            context.WindowHandle = _vulkanWindowHandle != IntPtr.Zero ? _vulkanWindowHandle : context.WindowHandle;
            if (context.WindowHandle == IntPtr.Zero)
            {
                return;
            }

            if (!vulkanRenderer.Initialize(context))
            {
                return;
            }

            vulkanRenderer.NewFrame();
            vulkanRenderer.Render(context, ref hookArgs);
        }

        private void HandleDirectX10Present(IntPtr swapChain, uint syncInterval, uint flags)
        {
            if (!TryAcceptDirectX10SwapChain(swapChain))
            {
                return;
            }

            RenderDxgiFrame(swapChain);
        }

        private void HandleDirectX10BeforeResizeBuffers(IntPtr swapChain, uint bufferCount, uint width, uint height, int newFormat, uint swapChainFlags)
        {
            if (ShutdownRequested)
            {
                return;
            }

            if (_directX10SwapChainHandle != IntPtr.Zero && swapChain != _directX10SwapChainHandle)
            {
                return;
            }

            if (Renderer is IDxgiOverlayRenderer dxgiRenderer)
            {
                dxgiRenderer.OnBeforeResizeBuffers(swapChain);
            }
        }

        private void HandleDirectX10AfterResizeBuffers(IntPtr swapChain, uint bufferCount, uint width, uint height, int newFormat, uint swapChainFlags, int hr)
        {
            if (ShutdownRequested || hr < 0)
            {
                return;
            }

            if (_directX10SwapChainHandle != IntPtr.Zero && swapChain != _directX10SwapChainHandle)
            {
                return;
            }

            if (Renderer is IDxgiOverlayRenderer dxgiRenderer)
            {
                dxgiRenderer.OnAfterResizeBuffers(swapChain);
            }
        }

        private void HandleDirectX11Present(IntPtr swapChain, uint syncInterval, uint flags)
        {
            if (!TryAcceptDirectX11SwapChain(swapChain))
            {
                return;
            }

            RenderDxgiFrame(swapChain);
        }

        private void HandleDirectX11BeforeResizeBuffers(IntPtr swapChain, uint bufferCount, uint width, uint height, int newFormat, uint swapChainFlags)
        {
            if (ShutdownRequested)
            {
                return;
            }

            if (_directX11SwapChainHandle != IntPtr.Zero && swapChain != _directX11SwapChainHandle)
            {
                return;
            }

            if (Renderer is IDxgiOverlayRenderer dxgiRenderer)
            {
                dxgiRenderer.OnBeforeResizeBuffers(swapChain);
            }
        }

        private void HandleDirectX11AfterResizeBuffers(IntPtr swapChain, uint bufferCount, uint width, uint height, int newFormat, uint swapChainFlags, int hr)
        {
            if (ShutdownRequested || hr < 0)
            {
                return;
            }

            if (_directX11SwapChainHandle != IntPtr.Zero && swapChain != _directX11SwapChainHandle)
            {
                return;
            }

            if (Renderer is IDxgiOverlayRenderer dxgiRenderer)
            {
                dxgiRenderer.OnAfterResizeBuffers(swapChain);
            }
        }

        private void HandleDirectX12Present(IntPtr swapChain, uint syncInterval, uint flags)
        {
            if (!TryAcceptDirectX12SwapChain(swapChain))
            {
                return;
            }

            RenderDxgiFrame(swapChain);
        }

        private void HandleDirectX12ResizeBuffers(IntPtr swapChain, uint bufferCount, uint width, uint height, int newFormat, uint swapChainFlags)
        {
            if (ShutdownRequested)
            {
                return;
            }

            if (_directX12SwapChainHandle != IntPtr.Zero && swapChain != _directX12SwapChainHandle)
            {
                return;
            }

            if (Renderer is IDxgiOverlayRenderer dxgiRenderer)
            {
                dxgiRenderer.OnBeforeResizeBuffers(swapChain);
            }
        }

        private void RenderDxgiFrame(IntPtr swapChain)
        {
            if (ShutdownRequested || swapChain == IntPtr.Zero)
            {
                return;
            }

            if (!(Renderer is IDxgiOverlayRenderer dxgiRenderer))
            {
                return;
            }

            IntPtr dxgiWindowHandle = Renderer.GraphicsApi switch
            {
                GraphicsApi.DirectX10 => _directX10WindowHandle,
                GraphicsApi.DirectX11 => _directX11WindowHandle,
                GraphicsApi.DirectX12 => _directX12WindowHandle,
                _ => IntPtr.Zero,
            };

            if (!Renderer.IsInitialized && !dxgiRenderer.InitializeFromSwapChain(swapChain, dxgiWindowHandle))
            {
                return;
            }

            dxgiRenderer.NewFrame();
            dxgiRenderer.Render(swapChain);
        }

        private bool TryAcceptDirectX10SwapChain(IntPtr swapChain)
        {
            if (swapChain == IntPtr.Zero || _directX10Hook == null)
            {
                return false;
            }

            int currentThreadId = GetCurrentThreadId();
            if (_directX10SwapChainHandle != IntPtr.Zero)
            {
                if (swapChain != _directX10SwapChainHandle)
                {
                    return false;
                }

                if (_directX10PresentThreadId == 0)
                {
                    _directX10PresentThreadId = currentThreadId;
                    return true;
                }

                if (currentThreadId == _directX10PresentThreadId)
                {
                    return true;
                }

                if (!_directX10LoggedThreadMismatch)
                {
                    _directX10LoggedThreadMismatch = true;
                    Console.WriteLine($"[PhantomRender] DX10: ignoring Present from thread {currentThreadId}, owner thread is {_directX10PresentThreadId}.");
                }

                return false;
            }

            IntPtr outputWindow = IntPtr.Zero;
            _directX10Hook.TryGetOutputWindow(swapChain, out outputWindow);
            outputWindow = ResolveDxgiWindowHandle(outputWindow);
            if (outputWindow == IntPtr.Zero)
            {
                return false;
            }

            IntPtr preferredWindow = ResolveWindowHandle();
            if (preferredWindow != IntPtr.Zero && outputWindow != preferredWindow)
            {
                return false;
            }

            _directX10SwapChainHandle = swapChain;
            _directX10WindowHandle = outputWindow;
            _directX10PresentThreadId = currentThreadId;
            _directX10LoggedThreadMismatch = false;
            return true;
        }

        private bool TryAcceptDirectX9Device(IntPtr device, IntPtr windowHandle)
        {
            if (device == IntPtr.Zero || windowHandle == IntPtr.Zero)
            {
                return false;
            }

            int currentThreadId = GetCurrentThreadId();
            IntPtr rootWindow = GetAncestor(windowHandle, GA_ROOT);
            if (rootWindow == IntPtr.Zero)
            {
                rootWindow = windowHandle;
            }

            if (_directX9DeviceHandle != IntPtr.Zero)
            {
                if (device != _directX9DeviceHandle)
                {
                    return false;
                }

                if (_directX9WindowHandle != IntPtr.Zero && rootWindow != _directX9WindowHandle)
                {
                    return false;
                }

                if (_directX9PresentThreadId == 0)
                {
                    _directX9PresentThreadId = currentThreadId;
                    return true;
                }

                if (currentThreadId == _directX9PresentThreadId)
                {
                    return true;
                }

                if (!_directX9LoggedThreadMismatch)
                {
                    _directX9LoggedThreadMismatch = true;
                    Console.WriteLine($"[PhantomRender] DX9: ignoring callback from thread {currentThreadId}, owner thread is {_directX9PresentThreadId}.");
                }

                return false;
            }

            if (!IsWindow(rootWindow) || !IsWindowVisible(rootWindow))
            {
                return false;
            }

            IntPtr preferredWindow = ResolveWindowHandle();
            if (preferredWindow != IntPtr.Zero && rootWindow != preferredWindow)
            {
                return false;
            }

            _directX9DeviceHandle = device;
            _directX9WindowHandle = rootWindow;
            _directX9PresentThreadId = currentThreadId;
            _directX9LoggedThreadMismatch = false;
            return true;
        }

        private bool TryAcceptOpenGLTarget(IntPtr hdc, IntPtr windowHandle, IntPtr renderingContext)
        {
            if (hdc == IntPtr.Zero || windowHandle == IntPtr.Zero)
            {
                return false;
            }

            int currentThreadId = GetCurrentThreadId();
            IntPtr rootWindow = GetAncestor(windowHandle, GA_ROOT);
            if (rootWindow == IntPtr.Zero)
            {
                rootWindow = windowHandle;
            }

            if (!IsWindow(rootWindow) || !IsWindowVisible(rootWindow))
            {
                return false;
            }

            IntPtr preferredWindow = ResolveWindowHandle();
            if (preferredWindow != IntPtr.Zero && rootWindow != preferredWindow)
            {
                return false;
            }

            if (_openGLWindowHandle != IntPtr.Zero)
            {
                if (_openGLPresentThreadId != 0 && currentThreadId != _openGLPresentThreadId)
                {
                    if (!_openGLLoggedThreadMismatch)
                    {
                        _openGLLoggedThreadMismatch = true;
                        Console.WriteLine($"[PhantomRender] OpenGL: ignoring SwapBuffers from thread {currentThreadId}, owner thread is {_openGLPresentThreadId}.");
                    }

                    return false;
                }

                bool targetChanged =
                    rootWindow != _openGLWindowHandle ||
                    hdc != _openGLDeviceContext ||
                    (renderingContext != IntPtr.Zero &&
                     _openGLRenderingContext != IntPtr.Zero &&
                     renderingContext != _openGLRenderingContext);

                if (!targetChanged)
                {
                    return true;
                }

                ResetOpenGLRendererForTargetChange(rootWindow, hdc, renderingContext);
            }

            UpdateOpenGLTarget(rootWindow, hdc, renderingContext, currentThreadId);
            return true;
        }

        private bool TryAcceptVulkanPresentTarget(VulkanPresentContext context)
        {
            if (context.Device == IntPtr.Zero || context.Swapchain == IntPtr.Zero)
            {
                return false;
            }

            IntPtr windowHandle = context.WindowHandle;
            if (windowHandle == IntPtr.Zero)
            {
                windowHandle = ResolveWindowHandle();
            }

            if (windowHandle == IntPtr.Zero)
            {
                return false;
            }

            int currentThreadId = GetCurrentThreadId();
            IntPtr rootWindow = GetAncestor(windowHandle, GA_ROOT);
            if (rootWindow == IntPtr.Zero)
            {
                rootWindow = windowHandle;
            }

            if (!IsWindow(rootWindow) || !IsWindowVisible(rootWindow))
            {
                return false;
            }

            IntPtr preferredWindow = ResolveWindowHandle();
            if (preferredWindow != IntPtr.Zero && rootWindow != preferredWindow)
            {
                return false;
            }

            if (_vulkanSwapchainHandle != IntPtr.Zero)
            {
                if (_vulkanPresentThreadId != 0 && currentThreadId != _vulkanPresentThreadId)
                {
                    if (!_vulkanLoggedThreadMismatch)
                    {
                        _vulkanLoggedThreadMismatch = true;
                        Console.WriteLine($"[PhantomRender] Vulkan: ignoring Present from thread {currentThreadId}, owner thread is {_vulkanPresentThreadId}.");
                    }

                    return false;
                }

                bool targetChanged =
                    context.Device != _vulkanDeviceHandle ||
                    context.Swapchain != _vulkanSwapchainHandle ||
                    rootWindow != _vulkanWindowHandle;

                if (targetChanged && Renderer.IsInitialized)
                {
                    Console.WriteLine($"[PhantomRender] Vulkan: swap target changed (device=0x{context.Device.ToInt64():X}, swapchain=0x{context.Swapchain.ToInt64():X}), reinitializing renderer.");
                    Renderer.Dispose();
                }
            }

            _vulkanDeviceHandle = context.Device;
            _vulkanSwapchainHandle = context.Swapchain;
            _vulkanWindowHandle = rootWindow;
            _vulkanPresentThreadId = currentThreadId;
            _vulkanLoggedThreadMismatch = false;
            return true;
        }

        private void ResetOpenGLRendererForTargetChange(IntPtr windowHandle, IntPtr hdc, IntPtr renderingContext)
        {
            if (Renderer.IsInitialized)
            {
                Console.WriteLine(
                    $"[PhantomRender] OpenGL: swap target changed (HWND=0x{windowHandle.ToInt64():X}, HDC=0x{hdc.ToInt64():X}, HGLRC=0x{renderingContext.ToInt64():X}), reinitializing renderer.");
                Renderer.Dispose();
            }
        }

        private void UpdateOpenGLTarget(IntPtr windowHandle, IntPtr hdc, IntPtr renderingContext, int threadId)
        {
            _openGLWindowHandle = windowHandle;
            _openGLDeviceContext = hdc;
            _openGLRenderingContext = renderingContext;
            _openGLPresentThreadId = threadId;
            _openGLLoggedThreadMismatch = false;
        }

        private bool TryRenderOpenGLFrame(IntPtr deviceContext, IntPtr renderingContext, IntPtr windowHandle)
        {
            if (!TryEnterOpenGLContext(deviceContext, renderingContext, out IntPtr previousDeviceContext, out IntPtr previousRenderingContext, out bool switchedContext))
            {
                return false;
            }

            try
            {
                if (!Renderer.IsInitialized && !Initialize(deviceContext, windowHandle))
                {
                    return false;
                }

                BeginFrame();
                RenderFrame();
                return true;
            }
            finally
            {
                RestoreOpenGLContext(previousDeviceContext, previousRenderingContext, switchedContext);
            }
        }

        private static bool TryEnterOpenGLContext(
            IntPtr deviceContext,
            IntPtr renderingContext,
            out IntPtr previousDeviceContext,
            out IntPtr previousRenderingContext,
            out bool switchedContext)
        {
            previousRenderingContext = wglGetCurrentContext();
            previousDeviceContext = wglGetCurrentDC();
            switchedContext =
                previousDeviceContext != deviceContext ||
                previousRenderingContext != renderingContext;

            if (!switchedContext)
            {
                return true;
            }

            return wglMakeCurrent(deviceContext, renderingContext);
        }

        private static void RestoreOpenGLContext(IntPtr previousDeviceContext, IntPtr previousRenderingContext, bool switchedContext)
        {
            if (!switchedContext)
            {
                return;
            }

            if (previousDeviceContext != IntPtr.Zero && previousRenderingContext != IntPtr.Zero)
            {
                wglMakeCurrent(previousDeviceContext, previousRenderingContext);
                return;
            }

            wglMakeCurrent(IntPtr.Zero, IntPtr.Zero);
        }

        private IntPtr ResolveDxgiWindowHandle(IntPtr outputWindow)
        {
            if (outputWindow == IntPtr.Zero)
            {
                outputWindow = ResolveWindowHandle();
            }

            if (outputWindow == IntPtr.Zero || !IsWindow(outputWindow))
            {
                return IntPtr.Zero;
            }

            IntPtr rootWindow = GetAncestor(outputWindow, GA_ROOT);
            if (rootWindow == IntPtr.Zero)
            {
                rootWindow = outputWindow;
            }

            if (!IsWindow(rootWindow) || !IsWindowVisible(rootWindow))
            {
                return IntPtr.Zero;
            }

            return rootWindow;
        }

        private bool TryAcceptDirectX11SwapChain(IntPtr swapChain)
        {
            if (swapChain == IntPtr.Zero || _directX11Hook == null)
            {
                return false;
            }

            int currentThreadId = GetCurrentThreadId();
            if (_directX11SwapChainHandle != IntPtr.Zero)
            {
                if (swapChain != _directX11SwapChainHandle)
                {
                    return false;
                }

                if (_directX11PresentThreadId == 0)
                {
                    _directX11PresentThreadId = currentThreadId;
                    return true;
                }

                if (currentThreadId == _directX11PresentThreadId)
                {
                    return true;
                }

                if (!_directX11LoggedThreadMismatch)
                {
                    _directX11LoggedThreadMismatch = true;
                    Console.WriteLine($"[PhantomRender] DX11: ignoring Present from thread {currentThreadId}, owner thread is {_directX11PresentThreadId}.");
                }

                return false;
            }

            IntPtr outputWindow = IntPtr.Zero;
            _directX11Hook.TryGetOutputWindow(swapChain, out outputWindow);
            outputWindow = ResolveDxgiWindowHandle(outputWindow);
            if (outputWindow == IntPtr.Zero)
            {
                return false;
            }

            IntPtr preferredWindow = ResolveWindowHandle();
            if (preferredWindow != IntPtr.Zero && outputWindow != preferredWindow)
            {
                return false;
            }

            _directX11SwapChainHandle = swapChain;
            _directX11WindowHandle = outputWindow;
            _directX11PresentThreadId = currentThreadId;
            _directX11LoggedThreadMismatch = false;
            return true;
        }

        private bool TryAcceptDirectX12SwapChain(IntPtr swapChain)
        {
            if (swapChain == IntPtr.Zero || _directX12Hook == null)
            {
                return false;
            }

            int currentThreadId = GetCurrentThreadId();
            if (_directX12SwapChainHandle != IntPtr.Zero)
            {
                if (swapChain != _directX12SwapChainHandle)
                {
                    return false;
                }

                if (_directX12PresentThreadId == 0)
                {
                    _directX12PresentThreadId = currentThreadId;
                    return true;
                }

                if (currentThreadId == _directX12PresentThreadId)
                {
                    return true;
                }

                if (!_directX12LoggedThreadMismatch)
                {
                    _directX12LoggedThreadMismatch = true;
                    Console.WriteLine($"[PhantomRender] DX12: ignoring Present from thread {currentThreadId}, owner thread is {_directX12PresentThreadId}.");
                }

                return false;
            }

            IntPtr outputWindow = IntPtr.Zero;
            _directX12Hook.TryGetOutputWindow(swapChain, out outputWindow);
            outputWindow = ResolveDxgiWindowHandle(outputWindow);
            if (outputWindow == IntPtr.Zero)
            {
                return false;
            }

            IntPtr preferredWindow = ResolveWindowHandle();
            if (preferredWindow != IntPtr.Zero && outputWindow != preferredWindow)
            {
                return false;
            }

            _directX12SwapChainHandle = swapChain;
            _directX12WindowHandle = outputWindow;
            _directX12PresentThreadId = currentThreadId;
            _directX12LoggedThreadMismatch = false;
            return true;
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

        private void ValidateDxgiRenderer(GraphicsApi graphicsApi)
        {
            if (!(Renderer is IDxgiOverlayRenderer))
            {
                throw new InvalidOperationException($"{graphicsApi.ToDisplayName()} internal overlay requires a DXGI renderer.");
            }
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
                _directX9DeviceHandle = IntPtr.Zero;
                _directX9WindowHandle = IntPtr.Zero;
                _directX9PresentThreadId = 0;
                _directX9LoggedThreadMismatch = false;
            }

            if (_directX10Hook != null)
            {
                _directX10Hook.OnPresent -= HandleDirectX10Present;
                _directX10Hook.OnBeforeResizeBuffers -= HandleDirectX10BeforeResizeBuffers;
                _directX10Hook.OnAfterResizeBuffers -= HandleDirectX10AfterResizeBuffers;

                try
                {
                    _directX10Hook.Dispose();
                }
                catch
                {
                }

                _directX10Hook = null;
                _directX10SwapChainHandle = IntPtr.Zero;
                _directX10WindowHandle = IntPtr.Zero;
                _directX10PresentThreadId = 0;
                _directX10LoggedThreadMismatch = false;
            }

            if (_directX11Hook != null)
            {
                _directX11Hook.OnPresent -= HandleDirectX11Present;
                _directX11Hook.OnBeforeResizeBuffers -= HandleDirectX11BeforeResizeBuffers;
                _directX11Hook.OnAfterResizeBuffers -= HandleDirectX11AfterResizeBuffers;

                try
                {
                _directX11Hook.Dispose();
                }
                catch
                {
                }

                _directX11Hook = null;
                _directX11SwapChainHandle = IntPtr.Zero;
                _directX11WindowHandle = IntPtr.Zero;
                _directX11PresentThreadId = 0;
                _directX11LoggedThreadMismatch = false;
            }

            if (_directX12Hook != null)
            {
                _directX12Hook.OnPresent -= HandleDirectX12Present;
                _directX12Hook.OnResizeBuffers -= HandleDirectX12ResizeBuffers;

                try
                {
                    _directX12Hook.Dispose();
                }
                catch
                {
                }

                _directX12Hook = null;
                _directX12SwapChainHandle = IntPtr.Zero;
                _directX12WindowHandle = IntPtr.Zero;
                _directX12PresentThreadId = 0;
                _directX12LoggedThreadMismatch = false;
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
                _openGLDeviceContext = IntPtr.Zero;
                _openGLRenderingContext = IntPtr.Zero;
                _openGLWindowHandle = IntPtr.Zero;
                _openGLPresentThreadId = 0;
                _openGLLoggedThreadMismatch = false;
            }

            if (_vulkanHook != null)
            {
                _vulkanHook.OnPresent -= HandleVulkanPresent;

                try
                {
                    _vulkanHook.Dispose();
                }
                catch
                {
                }

                _vulkanHook = null;
                _vulkanDeviceHandle = IntPtr.Zero;
                _vulkanSwapchainHandle = IntPtr.Zero;
                _vulkanWindowHandle = IntPtr.Zero;
                _vulkanPresentThreadId = 0;
                _vulkanLoggedThreadMismatch = false;
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
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern IntPtr GetAncestor(IntPtr hWnd, uint gaFlags);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("kernel32.dll")]
        private static extern int GetCurrentThreadId();

        [DllImport("opengl32.dll")]
        private static extern IntPtr wglGetCurrentContext();

        [DllImport("opengl32.dll")]
        private static extern IntPtr wglGetCurrentDC();

        [DllImport("opengl32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool wglMakeCurrent(IntPtr hdc, IntPtr hglrc);

        private const uint GA_ROOT = 2;
    }
}
