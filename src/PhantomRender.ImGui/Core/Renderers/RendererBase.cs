using System;
using System.Runtime.ExceptionServices;
using Hexa.NET.ImGui;
using Hexa.NET.ImGui.Backends.Win32;
using PhantomRender.Core;
using PhantomRender.ImGui.Core;
using HexaImGui = Hexa.NET.ImGui.ImGui;

namespace PhantomRender.ImGui.Core.Renderers
{
    public abstract class RendererBase : IOverlayRenderer
    {
        private readonly object _callbackSync = new object();
        private Overlay _overlay;
        private Action[] _overlayNewFrameHandlers = Array.Empty<Action>();
        private Action[] _overlayRenderHandlers = Array.Empty<Action>();

        protected RendererBase(GraphicsApi graphicsApi)
        {
            GraphicsApi = graphicsApi;
        }

        public GraphicsApi GraphicsApi { get; }

        public bool IsInitialized { get; protected set; }

        public nint WindowHandle { get; protected set; }

        public ImGuiContextPtr Context { get; protected set; }

        public ImGuiIOPtr IO { get; protected set; }

        public event Action OnOverlayNewFrame
        {
            add => AddHandler(ref _overlayNewFrameHandlers, value);
            remove => RemoveHandler(ref _overlayNewFrameHandlers, value);
        }

        public event Action OnOverlayRender
        {
            add => AddHandler(ref _overlayRenderHandlers, value);
            remove => RemoveHandler(ref _overlayRenderHandlers, value);
        }

        public abstract bool Initialize(nint device, nint windowHandle);

        public abstract void NewFrame();

        public abstract void Render();

        public abstract void OnLostDevice();

        public abstract void OnResetDevice();

        public abstract void Dispose();

        internal void Attach(Overlay overlay)
        {
            if (overlay == null)
            {
                throw new ArgumentNullException(nameof(overlay));
            }

            if (_overlay == null)
            {
                _overlay = overlay;
                return;
            }

            if (!ReferenceEquals(_overlay, overlay))
            {
                throw new InvalidOperationException("Renderer is already attached to a different overlay.");
            }
        }

        protected void RaiseRendererInitializing(nint device, nint windowHandle)
        {
            try
            {
                RequireOverlay().RaiseRendererInitializing(this, device, windowHandle);
            }
            catch (Exception ex)
            {
                HandleOverlayCallbackException("RendererInitializing", ex);
            }
        }

        protected void RaiseImGuiInitialized()
        {
            try
            {
                RequireOverlay().RaiseImGuiInitialized(this);
            }
            catch (Exception ex)
            {
                HandleOverlayCallbackException("ImGuiInitialized", ex);
            }
        }

        protected void RaiseNewFrame()
        {
            try
            {
                RequireOverlay().RaiseNewFrame(this, GraphicsApi, WindowHandle);
            }
            catch (Exception ex)
            {
                HandleOverlayCallbackException("NewFrame", ex);
            }
        }

        protected void RaiseRender()
        {
            try
            {
                RequireOverlay().RaiseRender(this, GraphicsApi, WindowHandle);
            }
            catch (Exception ex)
            {
                HandleOverlayCallbackException("Render", ex);
            }
        }

        protected void RaiseOverlayNewFrame()
        {
            RaiseHandlers(_overlayNewFrameHandlers, "OnOverlayNewFrame");
        }

        protected void RaiseOverlayRender()
        {
            RaiseHandlers(_overlayRenderHandlers, "OnOverlayRender");
        }

        protected unsafe void InitializeImGui(nint windowHandle)
        {
            WindowHandle = windowHandle;

            Context = HexaImGui.CreateContext();
            HexaImGui.SetCurrentContext(Context);
            IO = HexaImGui.GetIO();
            IO.ConfigFlags |= ImGuiConfigFlags.NavEnableKeyboard;

            ImGuiImplWin32.SetCurrentContext(Context);
            ImGuiImplWin32.Init((void*)windowHandle);

            RaiseImGuiInitialized();
        }

        protected void BeginFrameCore(Action backendNewFrame)
        {
            if (backendNewFrame == null)
            {
                throw new ArgumentNullException(nameof(backendNewFrame));
            }

            SetSharedContextsCurrent();
            backendNewFrame();
            RaiseNewFrame();
            RaiseOverlayNewFrame();
            HexaImGui.NewFrame();
        }

        protected void RenderFrameCore(Action backendRender)
        {
            if (backendRender == null)
            {
                throw new ArgumentNullException(nameof(backendRender));
            }

            SetSharedContextsCurrent();
            RaiseRender();
            RaiseOverlayRender();
            HexaImGui.Render();
            backendRender();
        }

        protected void ShutdownImGui()
        {
            try
            {
                if (!Context.IsNull)
                {
                    HexaImGui.SetCurrentContext(Context);
                    ImGuiImplWin32.SetCurrentContext(Context);
                }
            }
            catch
            {
            }

            try
            {
                ImGuiImplWin32.Shutdown();
            }
            catch
            {
            }

            try
            {
                if (!Context.IsNull)
                {
                    HexaImGui.DestroyContext(Context);
                }
            }
            catch
            {
            }

            Context = ImGuiContextPtr.Null;
            IO = default;
            WindowHandle = IntPtr.Zero;
        }

        protected void ReportRuntimeError(string stage, Exception exception)
        {
            try
            {
                RequireOverlay().ReportRuntimeError(stage, exception);
            }
            catch
            {
            }
        }

        protected void SetSharedContextsCurrent()
        {
            if (Context.IsNull)
            {
                return;
            }

            HexaImGui.SetCurrentContext(Context);
            ImGuiImplWin32.SetCurrentContext(Context);
        }

        private void HandleOverlayCallbackException(string stage, Exception exception)
        {
            if (!RequireOverlay().CatchCallbackExceptions)
            {
                ExceptionDispatchInfo.Capture(exception).Throw();
            }

            ReportRuntimeError(stage, exception);
        }

        private Overlay RequireOverlay()
        {
            return _overlay ?? throw new InvalidOperationException("Renderer is not attached to an overlay.");
        }

        private void RaiseHandlers(Action[] handlers, string stage)
        {
            if (handlers == null || handlers.Length == 0)
            {
                return;
            }

            for (int i = 0; i < handlers.Length; i++)
            {
                try
                {
                    handlers[i]();
                }
                catch (Exception ex)
                {
                    HandleOverlayCallbackException(stage, ex);
                }
            }
        }

        private void AddHandler(ref Action[] handlers, Action handler)
        {
            if (handler == null)
            {
                return;
            }

            lock (_callbackSync)
            {
                int length = handlers.Length;
                Action[] updated = new Action[length + 1];
                Array.Copy(handlers, updated, length);
                updated[length] = handler;
                handlers = updated;
            }
        }

        private void RemoveHandler(ref Action[] handlers, Action handler)
        {
            if (handler == null)
            {
                return;
            }

            lock (_callbackSync)
            {
                int index = Array.IndexOf(handlers, handler);
                if (index < 0)
                {
                    return;
                }

                if (handlers.Length == 1)
                {
                    handlers = Array.Empty<Action>();
                    return;
                }

                Action[] updated = new Action[handlers.Length - 1];
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
