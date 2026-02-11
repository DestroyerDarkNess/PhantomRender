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

        public static void Initialize()
        {
            try { InitializeDX9(); }
            catch (Exception ex) { Console.WriteLine($"[PhantomRender] DX9 Init Failed: {ex}"); }

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

                _dx9Hook.Enable();
                Console.WriteLine("[PhantomRender] DX9 Hook Enabled.");
            }
            else
            {
                 Console.WriteLine("[PhantomRender] DX9 Device Not Found (Dummy creation failed).");
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
