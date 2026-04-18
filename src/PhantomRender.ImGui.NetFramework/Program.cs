using System;
using System.IO;
using PhantomRender.Core;
using PhantomRender.Overlays;
using PhantomRender.ImGui;
using PhantomRender.ImGui.Core.Renderers;
using System.Drawing;
using System.Windows.Forms;

namespace PhantomRender.ImGui.NetFramework
{
    public static class Program
    {
        public static int Main(string[] args)
        {
            GraphicsApi graphicsApi = ParseGraphicsApi(args);
            if (graphicsApi != GraphicsApi.DirectX9)
            {
                Console.WriteLine("[PhantomRender] External mode currently supports DX9 only.");
                return 1;
            }

            ExternalOverlayMode mode = ParseOverlayMode(args);
            using (var host = new DirectX9ExternalOverlayHost
            {
                Title = "PhantomRender",
                Mode = mode,
                TopMost = mode == ExternalOverlayMode.Window,
                ClickThrough = false,
                BackgroundColor = OverlayColor.Black,
                TransparentColor = OverlayColor.Black,
            })

            using (var overlay = new ExternalOverlay(new DirectX9Renderer()))
            {
                Rectangle screenBounds = Screen.PrimaryScreen.Bounds;
                host.Window.X = 0;
                host.Window.Y = 0;
                host.Window.Width = screenBounds.Width;
                host.Window.Height = screenBounds.Height;
                host.Window.ShowMaximizeBox = true;
                host.Window.Borderless = true;

                string assemblyDirectory = HostPathResolver.ResolveInjectedHostDirectory("PhantomRender.ImGui.NetFramework.dll");
                if (!overlay.Dependencies.LoadDependencies(assemblyDirectory))
                {
                    Console.WriteLine("[PhantomRender] Failed to load native ImGui dependencies.");
                    return 1;
                }

                bool initializationFailed = false;
                using (var ui = new UI(overlay, host.Window))
                {
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
                        Console.WriteLine("[PhantomRender] Failed to run the DX9 external overlay host.");
                        return 1;
                    }
                }

                return initializationFailed ? 1 : 0;
            }
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