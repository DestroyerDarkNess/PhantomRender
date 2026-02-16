using System;
using PhantomRender.ImGui;

namespace PhantomRender.ImGui.Native
{
    internal sealed class NativeRuntimeHost
    {
        private readonly INativeDependencyLoader _dependencyLoader;
        private readonly INativeOverlayBootstrap _overlayBootstrap;

        public NativeRuntimeHost(INativeDependencyLoader dependencyLoader, INativeOverlayBootstrap overlayBootstrap)
        {
            _dependencyLoader = dependencyLoader ?? throw new ArgumentNullException(nameof(dependencyLoader));
            _overlayBootstrap = overlayBootstrap ?? throw new ArgumentNullException(nameof(overlayBootstrap));
        }

        public void Initialize(IntPtr hModule)
        {
            try
            {
                // Mirror all console output into a per-game log file (e.g. "witcher3.log")
                // so we still have the trace when the game exits/crashes.
                ConsoleFileLog.Install(hModule);

                // Best-effort crash logging (managed unhandled + native SEH).
                CrashHandlers.Install();

                _dependencyLoader.LoadDependencies(hModule);

                Console.WriteLine("[PhantomRender] Initializing OverlayManager...");

                OverlayMenu menu = OverlayMenu.Default;
                _overlayBootstrap.Initialize(menu);

                Console.WriteLine("[PhantomRender] OverlayManager Initialized.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[PhantomRender] Initialization Error: {ex}");
            }
        }

        public void Shutdown()
        {
            try
            {
                _overlayBootstrap.Shutdown();
            }
            catch
            {
                // Best-effort cleanup.
            }
        }
    }
}
