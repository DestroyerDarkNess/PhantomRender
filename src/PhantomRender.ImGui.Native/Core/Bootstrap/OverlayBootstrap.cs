using System;
using PhantomRender.ImGui;
using PhantomRender.ImGui.Native.UI;

namespace PhantomRender.ImGui.Native
{
    public static class OverlayBootstrap
    {
        private static readonly object _sync = new object();
        private static OverlayMenu _menu;
        private static DefaultOverlayUi _defaultUi;
        private static InputEmulation _inputEmulation;

        public static void Initialize(OverlayMenu menu)
        {
            if (menu == null)
            {
                throw new ArgumentNullException(nameof(menu));
            }

            lock (_sync)
            {
                if (!ReferenceEquals(_menu, menu))
                {
                    Unsubscribe(_menu);
                    _menu = menu;
                    Subscribe(_menu);

                    try { _defaultUi?.Dispose(); } catch { }
                    _defaultUi = new DefaultOverlayUi(_menu);
                    Console.WriteLine("[PhantomRender] Default overlay UI starts hidden. Press Insert to toggle visibility.");
                    Console.Out.Flush();

                    try { _inputEmulation?.Dispose(); } catch { }
                    _inputEmulation = new InputEmulation(_menu, IsMenuVisible, ToggleMenuVisibility);
                }

                OverlayManager.Initialize(_menu);
            }
        }

        public static void Shutdown()
        {
            lock (_sync)
            {
                Unsubscribe(_menu);
                _menu = null;

                try { _inputEmulation?.Dispose(); } catch { }
                _inputEmulation = null;

                try { _defaultUi?.Dispose(); } catch { }
                _defaultUi = null;
            }
        }

        private static bool IsMenuVisible()
        {
            return _defaultUi != null && _defaultUi.Visible;
        }

        private static void ToggleMenuVisibility()
        {
            if (_defaultUi == null)
            {
                return;
            }

            _defaultUi.Visible = !_defaultUi.Visible;
        }

        private static void Subscribe(OverlayMenu menu)
        {
            if (menu == null)
            {
                return;
            }

            menu.InitializeRenderer += OnInitializeRenderer;
            menu.InitializeImGui += OnInitializeImGui;
            menu.OnError += OnOverlayError;
        }

        private static void Unsubscribe(OverlayMenu menu)
        {
            if (menu == null)
            {
                return;
            }

            menu.InitializeRenderer -= OnInitializeRenderer;
            menu.InitializeImGui -= OnInitializeImGui;
            menu.OnError -= OnOverlayError;
        }

        private static void OnInitializeRenderer(object sender, OverlayRendererInitializingEventArgs e)
        {
            try
            {
                Console.WriteLine($"[PhantomRender] InitializeRenderer: Api={e.Renderer.GraphicsApi}, Device=0x{e.Device.ToInt64():X}, Window=0x{e.WindowHandle.ToInt64():X}");
                Console.Out.Flush();
            }
            catch { }
        }

        private static void OnInitializeImGui(object sender, OverlayImGuiInitializedEventArgs e)
        {
            try
            {
                string contextState = e.Context.IsNull ? "null" : "ready";
                Console.WriteLine($"[PhantomRender] InitializeImGui: Context={contextState}, Display={e.IO.DisplaySize.X}x{e.IO.DisplaySize.Y}");
                Console.Out.Flush();
            }
            catch { }
        }

        private static void OnOverlayError(object sender, OverlayErrorEventArgs e)
        {
            try
            {
                Console.WriteLine($"[PhantomRender] Overlay error event ({e.Stage}): {e.Exception}");
                Console.Out.Flush();
            }
            catch { }
        }
    }
}
