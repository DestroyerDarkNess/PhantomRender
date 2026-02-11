using System;
using System.Runtime.InteropServices;
using MinHook;

namespace PhantomRender.Core.Hooks.Graphics.OpenGL
{
    public class OpenGLHook : IDisposable
    {
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        public delegate int wglSwapBuffersDelegate(IntPtr hdc);

        public event Action<IntPtr> OnSwapBuffers;

        private HookEngine _hookEngine;
        private wglSwapBuffersDelegate _originalSwapBuffers;

        [DllImport("gdi32.dll", EntryPoint = "SwapBuffers", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GdiSwapBuffers(IntPtr hdc);

        public OpenGLHook()
        {
            _hookEngine = new HookEngine();
            _originalSwapBuffers = _hookEngine.CreateHook<wglSwapBuffersDelegate>("opengl32.dll", "wglSwapBuffers", new wglSwapBuffersDelegate(SwapBuffersHook));
        }

        public void Enable()
        {
            _hookEngine.EnableHook(_originalSwapBuffers);
            Console.WriteLine("[PhantomRender] OpenGL wglSwapBuffers Hook Enabled (MinHook NuGet).");
        }

        public void Disable()
        {
            _hookEngine.DisableHook(_originalSwapBuffers);
        }

        private int SwapBuffersHook(IntPtr hdc)
        {
            try
            {
                OnSwapBuffers?.Invoke(hdc);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[PhantomRender] OpenGL SwapBuffers error: {ex.Message}");
            }

            // Using GdiSwapBuffers directly as it's more stable for wglSwapBuffers hooks
            return GdiSwapBuffers(hdc) ? 1 : 0;
        }

        public void Dispose()
        {
            _hookEngine?.Dispose();
            GC.SuppressFinalize(this);
        }
    }
}
