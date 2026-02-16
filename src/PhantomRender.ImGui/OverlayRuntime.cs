using System;
using System.Reflection;

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

                Type nativeBootstrapType = Type.GetType("PhantomRender.ImGui.Native.NativeOverlayBootstrap, PhantomRender.ImGui.Native", throwOnError: false);
                if (nativeBootstrapType != null)
                {
                    MethodInfo nativeInit = nativeBootstrapType.GetMethod("Initialize", BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(OverlayMenu) }, null);
                    if (nativeInit == null)
                    {
                        throw new MissingMethodException("Initialize(OverlayMenu) was not found on NativeOverlayBootstrap.");
                    }

                    nativeInit.Invoke(null, new object[] { overlayMenu });
                }
                else
                {
                    OverlayManager.Initialize(overlayMenu);
                }

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
