using System;

namespace PhantomRender.ImGui.Native
{
    internal interface INativeDependencyLoader
    {
        void LoadDependencies(IntPtr hModule);
    }
}

