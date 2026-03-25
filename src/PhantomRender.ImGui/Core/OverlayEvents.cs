using System;
using Hexa.NET.ImGui;
using PhantomRender.ImGui.Core.Renderers;

namespace PhantomRender.ImGui.Core
{
    public sealed class OverlayRendererInitializingEventArgs : EventArgs
    {
        public OverlayRendererInitializingEventArgs(IOverlayRenderer renderer, nint device, nint windowHandle)
        {
            Renderer = renderer ?? throw new ArgumentNullException(nameof(renderer));
            Device = device;
            WindowHandle = windowHandle;
        }

        public IOverlayRenderer Renderer { get; }

        public nint Device { get; }

        public nint WindowHandle { get; }
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

    public sealed class OverlayFrameEventArgs : EventArgs
    {
        public OverlayFrameEventArgs(IOverlayRenderer renderer, GraphicsApi graphicsApi, nint windowHandle)
        {
            Renderer = renderer ?? throw new ArgumentNullException(nameof(renderer));
            GraphicsApi = graphicsApi;
            WindowHandle = windowHandle;
        }

        public IOverlayRenderer Renderer { get; }

        public GraphicsApi GraphicsApi { get; }

        public nint WindowHandle { get; }
    }

    public sealed class OverlayErrorEventArgs : EventArgs
    {
        public OverlayErrorEventArgs(string stage, Exception exception)
        {
            Stage = stage ?? throw new ArgumentNullException(nameof(stage));
            Exception = exception ?? throw new ArgumentNullException(nameof(exception));
        }

        public string Stage { get; }

        public Exception Exception { get; }
    }

    public sealed class OverlayWindowEventArgs : EventArgs
    {
        public OverlayWindowEventArgs(nint windowHandle)
        {
            WindowHandle = windowHandle;
        }

        public nint WindowHandle { get; }
    }
}
