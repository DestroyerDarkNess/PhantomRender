using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using PhantomRender.Core.Hooks.Graphics;
using PhantomRender.Core.Hooks.Graphics.OpenGL;
using PhantomRender.ImGui.Renderers;

namespace PhantomRender.ImGui
{
    public static class OverlayManager
    {
        private static DirectX9Hook _dx9Hook;
        private static DirectX9Renderer _dx9Renderer;

        private static OpenGLHook _glHook;
        private static OpenGLRenderer _glRenderer;
        private static bool _glInitFailed;

        private static DirectX10Hook _dx10Hook;
        private static DirectX10Renderer _dx10Renderer;

        public static void Initialize()
        {
            try { InitializeDX9(); }
            catch (Exception ex) { Console.WriteLine($"[PhantomRender] DX9 Init Failed: {ex}"); }

            try { InitializeDX10(); }
            catch (Exception ex) { Console.WriteLine($"[PhantomRender] DX10 Init Failed: {ex}"); }

            try { InitializeOpenGL(); }
            catch (Exception ex) { Console.WriteLine($"[PhantomRender] OpenGL Init Failed: {ex}"); }
        }

        private static object _dx9Lock = new object();

        private static void InitializeDX9()
        {
            var deviceAddr = DirectX9Hook.GetDeviceAddress();
            if (deviceAddr != IntPtr.Zero)
            {
                _dx9Hook = new DirectX9Hook(deviceAddr);
                _dx9Renderer = new DirectX9Renderer();
                
                _dx9Hook.OnEndScene += (device) =>
                {
                    if (!_dx9Renderer.IsInitialized)
                    {
                        lock (_dx9Lock)
                        {
                            if (!_dx9Renderer.IsInitialized)
                            {
                                Console.WriteLine("[PhantomRender] DX9 OnEndScene - Initializing Renderer... (Lock Acquired)");
                                IntPtr hWnd = GetWindowHandleFailSafe();
                                Console.WriteLine($"[PhantomRender] Target Window: {hWnd}");
                                _dx9Renderer.Initialize(device, hWnd);
                            }
                        }
                    }

                    if (_dx9Renderer.IsInitialized)
                    {
                        _dx9Renderer.NewFrame();
                        _dx9Renderer.Render();
                    }
                };

                _dx9Hook.OnBeforeReset += (device, pp) =>
                {
                    lock (_dx9Lock)
                    {
                        if (_dx9Renderer.IsInitialized)
                        {
                            Console.WriteLine("[PhantomRender] DX9 Pre-Reset: Invalidating device objects...");
                            _dx9Renderer.OnLostDevice();
                        }
                    }
                };

                _dx9Hook.OnAfterReset += (device, pp) =>
                {
                    lock (_dx9Lock)
                    {
                        if (_dx9Renderer.IsInitialized)
                        {
                            Console.WriteLine("[PhantomRender] DX9 Post-Reset: Recreating device objects...");
                            _dx9Renderer.OnResetDevice();
                        }
                    }
                };

                _dx9Hook.Enable();
                Console.WriteLine("[PhantomRender] DX9 Hook Enabled.");
            }
            else
            {
                 Console.WriteLine("[PhantomRender] DX9 Device Not Found (Dummy creation failed).");
            }
        }

        private static object _dx10Lock = new object();

        private static void InitializeDX10()
        {
            var swapChainAddr = DirectX10Hook.GetSwapChainAddress();
            if (swapChainAddr != IntPtr.Zero)
            {
                _dx10Hook = new DirectX10Hook(swapChainAddr);
                _dx10Renderer = new DirectX10Renderer();

                _dx10Hook.OnPresent += (swapChain, syncInterval, flags) =>
                {
                    if (!_dx10Renderer.IsInitialized)
                    {
                        lock (_dx10Lock)
                        {
                            if (!_dx10Renderer.IsInitialized)
                            {
                                Console.WriteLine("[PhantomRender] DX10 OnPresent - Initializing Renderer...");
                                IntPtr hWnd = GetWindowHandleFailSafe();
                                
                                IntPtr device = _dx10Hook.GetDevice(swapChain);
                                if (device != IntPtr.Zero)
                                {
                                    Console.WriteLine($"[PhantomRender] DX10 Device found: {device}. Target Window: {hWnd}");
                                    _dx10Renderer.Initialize(device, hWnd);
                                    // Release the extra ref from GetDevice
                                    Marshal.Release(device);
                                }
                                else
                                {
                                    Console.WriteLine("[PhantomRender] DX10 Device NOT found in SwapChain!");
                                }
                            }
                        }
                    }

                    if (_dx10Renderer.IsInitialized)
                    {
                        _dx10Renderer.NewFrame();
                        _dx10Renderer.Render();
                    }
                };
                
                _dx10Hook.Enable();
                Console.WriteLine("[PhantomRender] DX10 Hook Enabled.");
            }
        }

        private static void InitializeOpenGL()
        {
            _glHook = new OpenGLHook();
            _glRenderer = new OpenGLRenderer();

            _glHook.OnSwapBuffers += (hdc) =>
            {
                 if (!_glRenderer.IsInitialized && !_glInitFailed)
                 {
                     Console.WriteLine("[PhantomRender] OpenGL OnSwapBuffers - Initializing Renderer...");
                     IntPtr hWnd = WindowFromDC(hdc);
                     if (hWnd == IntPtr.Zero) hWnd = GetWindowHandleFailSafe();
                     Console.WriteLine($"[PhantomRender] Target Window: {hWnd}");
                     
                     if (!_glRenderer.Initialize(IntPtr.Zero, hWnd))
                     {
                         Console.WriteLine("[PhantomRender] OpenGL Renderer init failed, will not retry.");
                         _glInitFailed = true;
                     }
                 }

                 if (_glRenderer.IsInitialized)
                 {
                     _glRenderer.NewFrame();
                     _glRenderer.Render();
                 }
            };
            
            _glHook.Enable();
             Console.WriteLine("[PhantomRender] OpenGL Hook Enabled.");
        }

        [DllImport("user32.dll")]
        private static extern IntPtr WindowFromDC(IntPtr hdc);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        private static IntPtr GetWindowHandleFailSafe()
        {
            try
            {
                var process = Process.GetCurrentProcess();
                if (process.MainWindowHandle != IntPtr.Zero)
                    return process.MainWindowHandle;
                
                IntPtr fg = GetForegroundWindow();
                if (fg != IntPtr.Zero)
                {
                    GetWindowThreadProcessId(fg, out uint processId);
                    if (processId == process.Id)
                        return fg;
                }
            }
            catch { }
            
            return IntPtr.Zero;
        }
    }
}
