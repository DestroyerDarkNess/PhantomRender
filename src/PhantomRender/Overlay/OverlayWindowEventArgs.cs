using System;

namespace PhantomRender.Overlays
{
    public sealed class OverlayWindowEventArgs : EventArgs
    {
        public OverlayWindowEventArgs(nint windowHandle)
        {
            WindowHandle = windowHandle;
        }

        public nint WindowHandle { get; }
    }
}
