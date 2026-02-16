using System;
using Hexa.NET.ImGui;
using PhantomRender.ImGui.Renderers;

namespace PhantomRender.ImGui
{
    public sealed class OverlayRendererInitializingEventArgs : EventArgs
    {
        public OverlayRendererInitializingEventArgs(IOverlayRenderer renderer, IntPtr device, IntPtr windowHandle)
        {
            Renderer = renderer ?? throw new ArgumentNullException(nameof(renderer));
            Device = device;
            WindowHandle = windowHandle;
        }

        public IOverlayRenderer Renderer { get; }
        public IntPtr Device { get; }
        public IntPtr WindowHandle { get; }
    }

    public sealed class OverlayImGuiInitializedEventArgs : EventArgs
    {
        public OverlayImGuiInitializedEventArgs(IOverlayRenderer renderer, ImGuiContextPtr context, ImGuiIOPtr io)
        {
            Renderer = renderer ?? throw new ArgumentNullException(nameof(renderer));
            Context = context;
            IO = io;
        }

        public IOverlayRenderer Renderer { get; }
        public ImGuiContextPtr Context { get; }
        public ImGuiIOPtr IO { get; }
    }

    public sealed class OverlayRenderEventArgs : EventArgs
    {
        public OverlayRenderEventArgs(IOverlayRenderer renderer, Renderers.GraphicsApi api, IntPtr windowHandle, ulong frameCounter)
        {
            Renderer = renderer ?? throw new ArgumentNullException(nameof(renderer));
            Api = api;
            WindowHandle = windowHandle;
            FrameCounter = frameCounter;
        }

        public IOverlayRenderer Renderer { get; }
        public Renderers.GraphicsApi Api { get; }
        public IntPtr WindowHandle { get; }
        public ulong FrameCounter { get; }
    }

    public sealed class OverlayNewFrameEventArgs : EventArgs
    {
        public OverlayNewFrameEventArgs(IOverlayRenderer renderer, Renderers.GraphicsApi api, IntPtr windowHandle)
        {
            Renderer = renderer ?? throw new ArgumentNullException(nameof(renderer));
            Api = api;
            WindowHandle = windowHandle;
        }

        public IOverlayRenderer Renderer { get; }
        public Renderers.GraphicsApi Api { get; }
        public IntPtr WindowHandle { get; }
    }

    public sealed class OverlayErrorEventArgs : EventArgs
    {
        public OverlayErrorEventArgs(string stage, Exception exception)
        {
            Stage = stage ?? string.Empty;
            Exception = exception ?? throw new ArgumentNullException(nameof(exception));
        }

        public string Stage { get; }
        public Exception Exception { get; }
    }
}
