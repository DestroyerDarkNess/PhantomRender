using System;
using System.Runtime.InteropServices;
using PhantomRender.Core.Hooks;

namespace PhantomRender.Core.Hooks.Graphics.OpenGL
{
    public class OpenGLHook : IATHook
    {
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        public delegate int wglSwapBuffersDelegate(IntPtr hdc);

        private wglSwapBuffersDelegate _hookDelegate;
        public event Action<IntPtr> OnSwapBuffers;

        public OpenGLHook(string targetModule) 
            : base(targetModule, "opengl32.dll", "wglSwapBuffers", IntPtr.Zero)
        {
            _hookDelegate = new wglSwapBuffersDelegate(wglSwapBuffersHook);
            _newFunctionAddress = Marshal.GetFunctionPointerForDelegate(_hookDelegate);
        }

        private int wglSwapBuffersHook(IntPtr hdc)
        {
            OnSwapBuffers?.Invoke(hdc);

            if (OriginalFunction != IntPtr.Zero)
            {
                var original = Marshal.GetDelegateForFunctionPointer<wglSwapBuffersDelegate>(OriginalFunction);
                return original(hdc);
            }
            return 1; // TRUE
        }
    }
}
