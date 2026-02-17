using System;
using System.Threading;
using PhantomRender.ImGui.Renderers;

namespace PhantomRender.ImGui
{
    public sealed class OverlayMenu
    {
        private static OverlayMenu _default = new OverlayMenu();
        private int _raisingError;

        public OverlayMenu()
            : this(new OverlayMenuOptions())
        {
        }

        public OverlayMenu(OverlayHookKind preferredHook)
            : this(new OverlayMenuOptions { PreferredHook = preferredHook })
        {
        }

        public OverlayMenu(OverlayMenuOptions options)
        {
            Options = options ?? throw new ArgumentNullException(nameof(options));
        }

        public static OverlayMenu Default
        {
            get => Volatile.Read(ref _default);
            set => Volatile.Write(ref _default, value ?? throw new ArgumentNullException(nameof(value)));
        }

        public OverlayMenuOptions Options { get; }

        public event EventHandler<OverlayRendererInitializingEventArgs> InitializeRenderer;

        public event EventHandler<OverlayImGuiInitializedEventArgs> InitializeImGui;

        public event EventHandler<OverlayNewFrameEventArgs> NewFrame;

        public event EventHandler<OverlayRenderEventArgs> Render;

        public event EventHandler<OverlayErrorEventArgs> OnError;

        internal void RaiseRendererInitializing(IOverlayRenderer renderer, IntPtr device, IntPtr windowHandle)
        {
            DispatchSafe(
                InitializeRenderer,
                new OverlayRendererInitializingEventArgs(renderer, device, windowHandle),
                "InitializeRenderer");
        }

        internal void RaiseImGuiInitialized(IOverlayRenderer renderer)
        {
            DispatchSafe(
                InitializeImGui,
                new OverlayImGuiInitializedEventArgs(renderer, renderer.Context, renderer.IO),
                "InitializeImGui");
        }

        internal void RenderFrame(IOverlayRenderer renderer, GraphicsApi api, IntPtr windowHandle)
        {
            DispatchSafe(
                Render,
                new OverlayRenderEventArgs(renderer, api, windowHandle),
                "Render");
        }

        internal void RaiseNewFrame(IOverlayRenderer renderer, GraphicsApi api, IntPtr windowHandle)
        {
            DispatchSafe(
                NewFrame,
                new OverlayNewFrameEventArgs(renderer, api, windowHandle),
                "NewFrame");
        }

        internal void ReportRuntimeError(string stage, Exception exception)
        {
            ReportError(stage, exception);
        }

        private void DispatchSafe<T>(EventHandler<T> handlers, T args, string stage)
            where T : EventArgs
        {
            if (handlers == null)
            {
                return;
            }

            foreach (Delegate handler in handlers.GetInvocationList())
            {
                try
                {
                    ((EventHandler<T>)handler)(this, args);
                }
                catch (Exception ex)
                {
                    if (!Options.CatchUserCallbackExceptions)
                    {
                        throw;
                    }

                    ReportError(stage, ex);
                }
            }
        }

        private void ReportError(string stage, Exception exception)
        {
            try
            {
                Console.WriteLine($"[PhantomRender] OverlayMenu error ({stage}): {exception}");
                Console.Out.Flush();
            }
            catch
            {
                // Ignore logging failures.
            }

            EventHandler<OverlayErrorEventArgs> handlers = OnError;
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
                        // Avoid recursive error event loops.
                    }
                }
            }
            finally
            {
                Volatile.Write(ref _raisingError, 0);
            }
        }
    }
}