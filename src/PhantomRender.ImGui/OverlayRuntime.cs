using System;

namespace PhantomRender.ImGui
{
    /// <summary>
    /// Facade to bootstrap the overlay runtime from the public assembly.
    /// </summary>
    public static class OverlayRuntime
    {
        private static readonly object _sync = new object();
        private static bool _initialized;

        public static void Initialize()
        {
            Initialize(OverlayMenu.Default);
        }

        public static void Initialize(OverlayMenu overlayMenu)
        {
            if (overlayMenu == null)
            {
                throw new ArgumentNullException(nameof(overlayMenu));
            }

            OverlayMenu.Default = overlayMenu;

            lock (_sync)
            {
                if (_initialized)
                {
                    return;
                }

                OverlayManager.Initialize(overlayMenu);

                _initialized = true;
            }
        }

        public static bool TryInitialize(OverlayMenu overlayMenu, out Exception error)
        {
            try
            {
                Initialize(overlayMenu ?? OverlayMenu.Default);
                error = null;
                return true;
            }
            catch (Exception ex)
            {
                error = ex;
                return false;
            }
        }
    }
}
