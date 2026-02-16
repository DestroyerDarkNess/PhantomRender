using System;

namespace PhantomRender.ImGui.Native
{
    internal interface IDependencyLoader
    {
        void LoadDependencies(IntPtr hModule);
    }
}
