using System;
using PhantomRender.Core.Hooks;

namespace PhantomRender.Core.Hooks.Graphics.OpenGL
{
    public class OpenGLHook : IATHook
    {
        public OpenGLHook(string targetModule, IntPtr newFunctionAddress) 
            : base(targetModule, "opengl32.dll", "wglSwapBuffers", newFunctionAddress)
        {
        }
    }
}
