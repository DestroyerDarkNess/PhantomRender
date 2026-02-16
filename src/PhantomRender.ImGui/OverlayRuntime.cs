using System;
using System.Reflection;

namespace PhantomRender.ImGui
{
    /// <summary>
    /// Thin facade that boots the native implementation if PhantomRender.ImGui.Native is present.
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

                Type managerType = Type.GetType("PhantomRender.ImGui.Native.OverlayManager, PhantomRender.ImGui.Native", throwOnError: false);
                if (managerType == null)
                {
                    throw new InvalidOperationException(
                        "Native runtime not found. Ensure PhantomRender.ImGui.Native.dll is loaded.");
                }

                MethodInfo initWithMenu = managerType.GetMethod("Initialize", BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(OverlayMenu) }, null);
                MethodInfo initNoArgs = managerType.GetMethod("Initialize", BindingFlags.Public | BindingFlags.Static, null, Type.EmptyTypes, null);

                if (initWithMenu != null)
                {
                    initWithMenu.Invoke(null, new object[] { overlayMenu });
                }
                else if (initNoArgs != null)
                {
                    initNoArgs.Invoke(null, null);
                }
                else
                {
                    throw new MissingMethodException("Initialize method was not found on native OverlayManager.");
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

