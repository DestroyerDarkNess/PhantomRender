using System;
using System.Runtime.InteropServices;
using MinHook;

namespace PhantomRender.Core.Hooks.Graphics.OpenGL
{
    public enum OpenGLSwapBuffersHookTarget
    {
        Auto = 0,
        GdiSwapBuffers = 1,
        WglSwapBuffers = 2,
    }

    public class OpenGLHook : IDisposable
    {
        [UnmanagedFunctionPointer(CallingConvention.Winapi, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public delegate bool SwapBuffersDelegate(IntPtr hdc);

        public event Action<IntPtr> OnSwapBuffers;

        private readonly HookEngine _hookEngine;
        private readonly string _hookTargetName;
        private SwapBuffersDelegate _originalSwapBuffers;

        [ThreadStatic]
        private static int _swapBuffersDepth;

        public OpenGLHook(OpenGLSwapBuffersHookTarget hookTarget = OpenGLSwapBuffersHookTarget.Auto)
        {
            _hookEngine = new HookEngine();
            _hookTargetName = hookTarget switch
            {
                OpenGLSwapBuffersHookTarget.GdiSwapBuffers => CreateRequiredHook("gdi32.dll", "SwapBuffers", "gdi32!SwapBuffers"),
                OpenGLSwapBuffersHookTarget.WglSwapBuffers => CreateRequiredHook("opengl32.dll", "wglSwapBuffers", "opengl32!wglSwapBuffers"),
                _ => TryCreateHook("gdi32.dll", "SwapBuffers", "gdi32!SwapBuffers")
                    ? "gdi32!SwapBuffers"
                    : TryCreateHook("opengl32.dll", "wglSwapBuffers", "opengl32!wglSwapBuffers")
                        ? "opengl32!wglSwapBuffers"
                        : throw new InvalidOperationException("Unable to hook OpenGL SwapBuffers."),
            };
        }

        public void Enable()
        {
            _hookEngine.EnableHook(_originalSwapBuffers);
            Console.WriteLine($"[PhantomRender] OpenGL {_hookTargetName} hook enabled.");
        }

        public void Disable()
        {
            _hookEngine.DisableHook(_originalSwapBuffers);
        }

        private bool SwapBuffersHook(IntPtr hdc)
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

        private bool TryCreateHook(string moduleName, string exportName, string hookTargetName)
        {
            try
            {
                _originalSwapBuffers = _hookEngine.CreateHook<SwapBuffersDelegate>(moduleName, exportName, new SwapBuffersDelegate(SwapBuffersHook));
                return _originalSwapBuffers != null;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[PhantomRender] OpenGL: failed to hook {hookTargetName}: {ex.Message}");
                _originalSwapBuffers = null;
                return false;
            }
        }

        private string CreateRequiredHook(string moduleName, string exportName, string hookTargetName)
        {
            if (!TryCreateHook(moduleName, exportName, hookTargetName))
            {
                throw new InvalidOperationException($"Unable to hook {hookTargetName}.");
            }

            return hookTargetName;
        }
    }
}
