using System;
using PhantomRender.Core;
using PhantomRender.ImGui;
using PhantomRender.ImGui.Core.Renderers;
using PhantomRender.Overlays;

namespace $safeprojectname$
{
    public static class Program
    {
        public static int Main(string[] args)
        {
            GraphicsApi graphicsApi = ParseGraphicsApi(args);
            if (graphicsApi != GraphicsApi.DirectX9)
            {
                Console.WriteLine("[$safeprojectname$] External mode currently supports DX9 only.");
                return 1;
            }

            ExternalOverlayMode mode = ParseOverlayMode(args);
            using var host = new DirectX9ExternalOverlayHost
            {
                Title = "$safeprojectname$",
                Mode = mode,
                TopMost = mode == ExternalOverlayMode.Overlay,
                ClickThrough = false,
            };

            if (mode == ExternalOverlayMode.Overlay)
            {
                host.TransparentColor = OverlayColor.Black;
            }

            using var overlay = new ExternalOverlay(new DirectX9Renderer());
            if (!overlay.Dependencies.LoadDependencies())
            {
                Console.WriteLine("[$safeprojectname$] Failed to load native ImGui dependencies.");
                return 1;
            }

            using var ui = new UI(overlay, host.Window);

            bool initializationFailed = false;
            host.DeviceCreated += (_, e) =>
            {
                if (!overlay.Initialize(e.Device, e.WindowHandle))
                {
                    initializationFailed = true;
                    host.RequestShutdown();
                }
            };

            host.BeforeReset += (_, __) => overlay.OnLostDevice();
            host.AfterReset += (_, __) => overlay.OnResetDevice();
            host.FrameRendering += (_, __) =>
            {
                if (!overlay.Renderer.IsInitialized)
                {
                    return;
                }

                overlay.BeginFrame();
                overlay.RenderFrame();

                if (ui.ShutdownRequested)
                {
                    host.RequestShutdown();
                }
            };

            if (!host.Run(() => !ui.ShutdownRequested))
            {
                Console.WriteLine("[$safeprojectname$] Failed to run the DX9 external overlay host.");
                return 1;
            }

            return initializationFailed ? 1 : 0;
        }

        private static GraphicsApi ParseGraphicsApi(string[] args)
        {
            if (args == null)
            {
                return GraphicsApi.DirectX9;
            }

            foreach (string arg in args)
            {
                if (arg == null)
                {
                    continue;
                }

                if (arg.Equals("--opengl", StringComparison.OrdinalIgnoreCase) ||
                    arg.Equals("--api=opengl", StringComparison.OrdinalIgnoreCase))
                {
                    return GraphicsApi.OpenGL;
                }

                if (arg.Equals("--dx9", StringComparison.OrdinalIgnoreCase) ||
                    arg.Equals("--api=dx9", StringComparison.OrdinalIgnoreCase))
                {
                    return GraphicsApi.DirectX9;
                }
            }

            return GraphicsApi.DirectX9;
        }

        private static ExternalOverlayMode ParseOverlayMode(string[] args)
        {
            if (args != null)
            {
                foreach (string arg in args)
                {
                    if (arg == null)
                    {
                        continue;
                    }

                    if (arg.Equals("--overlay", StringComparison.OrdinalIgnoreCase) ||
                        arg.Equals("--mode=overlay", StringComparison.OrdinalIgnoreCase))
                    {
                        return ExternalOverlayMode.Overlay;
                    }
                }
            }

            return ExternalOverlayMode.Window;
        }
    }
}
