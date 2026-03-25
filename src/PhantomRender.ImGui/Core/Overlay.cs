using System;
using System.Threading;
using PhantomRender.Core;
using PhantomRender.ImGui.Core.Renderers;

namespace PhantomRender.ImGui.Core
{
    public abstract class Overlay : IDisposable
    {
        private int _raisingError;

        protected Overlay(GraphicsApi graphicsApi)
            : this(CreateDefaultRenderer(graphicsApi))
        {
        }

        protected Overlay(RendererBase renderer)
        {
            if (renderer == null)
            {
                throw new ArgumentNullException(nameof(renderer));
            }

            renderer.Attach(this);
            GraphicsApi = renderer.GraphicsApi;
            Dependencies = new DependencyResolver();
        }

        public GraphicsApi GraphicsApi { get; }

        public DependencyResolver Dependencies { get; }

        public bool CatchCallbackExceptions { get; set; } = true;

        public event EventHandler<OverlayRendererInitializingEventArgs> RendererInitializing;

        public event EventHandler<OverlayImGuiInitializedEventArgs> ImGuiInitialized;

        public event EventHandler<OverlayFrameEventArgs> NewFrame;

        public event EventHandler<OverlayFrameEventArgs> Render;

        public event EventHandler<OverlayErrorEventArgs> Error;

        protected static RendererBase CreateDefaultRenderer(GraphicsApi graphicsApi)
        {
            if (graphicsApi == GraphicsApi.Unknown)
            {
                throw new ArgumentOutOfRangeException(nameof(graphicsApi), "Overlay requires a concrete graphics API.");
            }

            switch (graphicsApi)
            {
                case GraphicsApi.DirectX9:
                    return new DirectX9Renderer();

                case GraphicsApi.DirectX11:
                // return new DirectX11Renderer();
#if NET5_0_OR_GREATER
                case GraphicsApi.DirectX12:
                    // return new DirectX12Renderer();
#endif
                case GraphicsApi.OpenGL:
                    return new OpenGLRenderer();

                default:
                    throw new NotSupportedException($"{graphicsApi.ToDisplayName()} does not have an ImGui renderer yet.");
            }
        }

        public virtual void Dispose()
        {
        }

        internal void RaiseRendererInitializing(IOverlayRenderer renderer, nint device, nint windowHandle)
        {
            DispatchSafe(
                RendererInitializing,
                new OverlayRendererInitializingEventArgs(renderer, device, windowHandle),
                "RendererInitializing");
        }

        internal void RaiseImGuiInitialized(IOverlayRenderer renderer)
        {
            DispatchSafe(
                ImGuiInitialized,
                new OverlayImGuiInitializedEventArgs(renderer, renderer.Context, renderer.IO),
                "ImGuiInitialized");
        }

        internal void RaiseNewFrame(IOverlayRenderer renderer, GraphicsApi graphicsApi, nint windowHandle)
        {
            DispatchSafe(
                NewFrame,
                new OverlayFrameEventArgs(renderer, graphicsApi, windowHandle),
                "NewFrame");
        }

        internal void RaiseRender(IOverlayRenderer renderer, GraphicsApi graphicsApi, nint windowHandle)
        {
            DispatchSafe(
                Render,
                new OverlayFrameEventArgs(renderer, graphicsApi, windowHandle),
                "Render");
        }

        internal void ReportRuntimeError(string stage, Exception exception)
        {
            try
            {
                Console.WriteLine($"[PhantomRender] Overlay error ({stage}): {exception}");
            }
            catch
            {
            }

            EventHandler<OverlayErrorEventArgs> handlers = Error;
            if (handlers == null)
            {
                return;
            }

            if (Interlocked.CompareExchange(ref _raisingError, 1, 0) != 0)
            {
                return;
            }

            try
            {
                var args = new OverlayErrorEventArgs(stage, exception);
                foreach (Delegate handler in handlers.GetInvocationList())
                {
                    try
                    {
                        ((EventHandler<OverlayErrorEventArgs>)handler)(this, args);
                    }
                    catch
                    {
                    }
                }
            }
            finally
            {
                Volatile.Write(ref _raisingError, 0);
            }
        }

        private void DispatchSafe<TEventArgs>(EventHandler<TEventArgs> handlers, TEventArgs args, string stage)
            where TEventArgs : EventArgs
        {
            if (handlers == null)
            {
                return;
            }

            foreach (Delegate handler in handlers.GetInvocationList())
            {
                try
                {
                    ((EventHandler<TEventArgs>)handler)(this, args);
                }
                catch (Exception ex)
                {
                    if (!CatchCallbackExceptions)
                    {
                        throw;
                    }

                    ReportRuntimeError(stage, ex);
                }
            }
        }
    }
}