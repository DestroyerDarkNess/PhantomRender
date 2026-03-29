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

        [ThreadStatic]
        private static int _swapBuffersDepth;

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
            if (_swapBuffersDepth > 0)
            {
                return _originalSwapBuffers(hdc);
            }

            _swapBuffersDepth++;
            try
            {
                try
                {
                    OnSwapBuffers?.Invoke(hdc);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[PhantomRender] OpenGL SwapBuffers error: {ex.Message}");
                }

                return _originalSwapBuffers(hdc);
            }
            finally
            {
                _swapBuffersDepth--;
            }
        }

        public void Dispose()
        {
            _hookEngine?.Dispose();
            GC.SuppressFinalize(this);
        }
    }
}
