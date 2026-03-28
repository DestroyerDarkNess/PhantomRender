using System;
using System.Threading;
using PhantomRender.Core;
using PhantomRender.ImGui.Core.Renderers;

namespace PhantomRender.ImGui.Core
{
    public abstract class Overlay : IDisposable
    {
        private readonly object _eventSync = new object();
        private int _raisingError;
        private EventHandler<OverlayRendererInitializingEventArgs>[] _rendererInitializingHandlers = Array.Empty<EventHandler<OverlayRendererInitializingEventArgs>>();
        private EventHandler<OverlayImGuiInitializedEventArgs>[] _imGuiInitializedHandlers = Array.Empty<EventHandler<OverlayImGuiInitializedEventArgs>>();
        private EventHandler<OverlayFrameEventArgs>[] _newFrameHandlers = Array.Empty<EventHandler<OverlayFrameEventArgs>>();
        private EventHandler<OverlayFrameEventArgs>[] _renderHandlers = Array.Empty<EventHandler<OverlayFrameEventArgs>>();
        private EventHandler<OverlayErrorEventArgs>[] _errorHandlers = Array.Empty<EventHandler<OverlayErrorEventArgs>>();

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

        public event EventHandler<OverlayRendererInitializingEventArgs> RendererInitializing
        {
            add => AddHandler(ref _rendererInitializingHandlers, value);
            remove => RemoveHandler(ref _rendererInitializingHandlers, value);
        }

        public event EventHandler<OverlayImGuiInitializedEventArgs> ImGuiInitialized
        {
            add => AddHandler(ref _imGuiInitializedHandlers, value);
            remove => RemoveHandler(ref _imGuiInitializedHandlers, value);
        }

        public event EventHandler<OverlayFrameEventArgs> NewFrame
        {
            add => AddHandler(ref _newFrameHandlers, value);
            remove => RemoveHandler(ref _newFrameHandlers, value);
        }

        public event EventHandler<OverlayFrameEventArgs> Render
        {
            add => AddHandler(ref _renderHandlers, value);
            remove => RemoveHandler(ref _renderHandlers, value);
        }

        public event EventHandler<OverlayErrorEventArgs> Error
        {
            add => AddHandler(ref _errorHandlers, value);
            remove => RemoveHandler(ref _errorHandlers, value);
        }

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

                case GraphicsApi.DirectX10:
                    return new DirectX10Renderer();

                case GraphicsApi.DirectX11:
                    return new DirectX11Renderer();

                case GraphicsApi.DirectX12:
                    return new DirectX12Renderer();

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
            EventHandler<OverlayRendererInitializingEventArgs>[] handlers = _rendererInitializingHandlers;
            if (handlers.Length == 0)
            {
                return;
            }

            DispatchSafe(
                handlers,
                new OverlayRendererInitializingEventArgs(renderer, device, windowHandle),
                "RendererInitializing");
        }

        internal void RaiseImGuiInitialized(IOverlayRenderer renderer)
        {
            EventHandler<OverlayImGuiInitializedEventArgs>[] handlers = _imGuiInitializedHandlers;
            if (handlers.Length == 0)
            {
                return;
            }

            DispatchSafe(
                handlers,
                new OverlayImGuiInitializedEventArgs(renderer, renderer.Context, renderer.IO),
                "ImGuiInitialized");
        }

        internal void RaiseNewFrame(IOverlayRenderer renderer, GraphicsApi graphicsApi, nint windowHandle)
        {
            EventHandler<OverlayFrameEventArgs>[] handlers = _newFrameHandlers;
            if (handlers.Length == 0)
            {
                return;
            }

            DispatchSafe(
                handlers,
                new OverlayFrameEventArgs(renderer, graphicsApi, windowHandle),
                "NewFrame");
        }

        internal void RaiseRender(IOverlayRenderer renderer, GraphicsApi graphicsApi, nint windowHandle)
        {
            EventHandler<OverlayFrameEventArgs>[] handlers = _renderHandlers;
            if (handlers.Length == 0)
            {
                return;
            }

            DispatchSafe(
                handlers,
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

            EventHandler<OverlayErrorEventArgs>[] handlers = _errorHandlers;
            if (handlers.Length == 0)
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
                for (int i = 0; i < handlers.Length; i++)
                {
                    try
                    {
                        handlers[i](this, args);
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

        private void DispatchSafe<TEventArgs>(EventHandler<TEventArgs>[] handlers, TEventArgs args, string stage)
            where TEventArgs : EventArgs
        {
            if (handlers == null || handlers.Length == 0)
            {
                return;
            }

            for (int i = 0; i < handlers.Length; i++)
            {
                try
                {
                    handlers[i](this, args);
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

        private void AddHandler<TEventArgs>(ref EventHandler<TEventArgs>[] handlers, EventHandler<TEventArgs> handler)
            where TEventArgs : EventArgs
        {
            if (handler == null)
            {
                return;
            }

            lock (_eventSync)
            {
                int length = handlers.Length;
                EventHandler<TEventArgs>[] updated = new EventHandler<TEventArgs>[length + 1];
                Array.Copy(handlers, updated, length);
                updated[length] = handler;
                handlers = updated;
            }
        }

        private void RemoveHandler<TEventArgs>(ref EventHandler<TEventArgs>[] handlers, EventHandler<TEventArgs> handler)
            where TEventArgs : EventArgs
        {
            if (handler == null)
            {
                return;
            }

            lock (_eventSync)
            {
                int index = Array.IndexOf(handlers, handler);
                if (index < 0)
                {
                    return;
                }

                if (handlers.Length == 1)
                {
                    handlers = Array.Empty<EventHandler<TEventArgs>>();
                    return;
                }

                EventHandler<TEventArgs>[] updated = new EventHandler<TEventArgs>[handlers.Length - 1];
                if (index > 0)
                {
                    Array.Copy(handlers, 0, updated, 0, index);
                }

                if (index < handlers.Length - 1)
                {
                    Array.Copy(handlers, index + 1, updated, index, handlers.Length - index - 1);
                }

                handlers = updated;
            }
        }
    }
}
