using System;
using System.Runtime.InteropServices;
using PhantomRender.Core.Hooks;

namespace PhantomRender.Core.Hooks.Graphics.OpenGL
{
    public class OpenGLHook : SimpleInlineHook
    {
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        public delegate int wglSwapBuffersDelegate(IntPtr hdc);

        private wglSwapBuffersDelegate _hookDelegate;
        public event Action<IntPtr> OnSwapBuffers;

        public OpenGLHook() 
            : base("opengl32.dll", "wglSwapBuffers")
        {
            _hookDelegate = new wglSwapBuffersDelegate(wglSwapBuffersHook);
            SetHook(Marshal.GetFunctionPointerForDelegate(_hookDelegate));
        }

        private int wglSwapBuffersHook(IntPtr hdc)
        {
            OnSwapBuffers?.Invoke(hdc);

            if (OriginalFunction != IntPtr.Zero)
            {
                var original = Marshal.GetDelegateForFunctionPointer<wglSwapBuffersDelegate>(OriginalFunction);
                return original(hdc);
            }
            return 1; // TRUE (Fail safe, though OriginalFunction should exist if Enabled)
        }
    }
}
