using System;
using Hexa.NET.ImGui;
using Hexa.NET.ImGui.Backends.D3D9;
using Hexa.NET.ImGui.Backends.Win32;
using PhantomRender.ImGui;

namespace PhantomRender.ImGui.Renderers
{
    public sealed class DirectX9Renderer : RendererBase
    {
        public DirectX9Renderer(OverlayMenu overlayMenu)
            : base(overlayMenu, GraphicsApi.DirectX9)
        {
        }

        public override unsafe bool Initialize(IntPtr device, IntPtr windowHandle)
        {
            if (IsInitialized) return true;

            try
            {
                Console.WriteLine($"[PhantomRender] DirectX9Renderer: Entering Initialize (RE-PUBLISHED V2). Device: {device}, Window: {windowHandle}");
                Console.Out.Flush();

                RaiseRendererInitializing(device, windowHandle);
                InitializeImGui(windowHandle);
                
                // Synchronize context
                Console.WriteLine("[PhantomRender] DirectX9Renderer: Setting context for D3D9 backend...");
                Console.Out.Flush();
                ImGuiImplD3D9.SetCurrentContext(Context);

                // Initialize D3D9 Backend
                Console.WriteLine("[PhantomRender] DirectX9Renderer: Calling ImGuiImplD3D9.Init...");
                Console.Out.Flush();
                
                if (!ImGuiImplD3D9.Init((IDirect3DDevice9*)device))
                {
                    Console.WriteLine("[PhantomRender] DirectX9Renderer: ImGuiImplD3D9.Init returned FALSE!");
                    Console.Out.Flush();
                    ShutdownImGui();
                    return false;
                }

                IsInitialized = true;
                Console.WriteLine("[PhantomRender] DirectX9Renderer: Initialized Successfully! (V2)");
                Console.Out.Flush();
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[PhantomRender] DirectX9Renderer: Init Error (V2): {ex}");
                Console.Out.Flush();
                return false;
            }
        }

        public override void NewFrame()
        {
            if (!IsInitialized) return;

            Hexa.NET.ImGui.ImGui.SetCurrentContext(Context);
            ImGuiImplD3D9.SetCurrentContext(Context);
            ImGuiImplWin32.SetCurrentContext(Context);

            ImGuiImplD3D9.NewFrame();
            ImGuiImplWin32.NewFrame();
            RaiseNewFrame();
            Hexa.NET.ImGui.ImGui.NewFrame();
        }

        private ulong _frameCounter;

        public override void Render()
        {
            if (!IsInitialized) return;

            _frameCounter++;

            Hexa.NET.ImGui.ImGui.SetCurrentContext(Context);
            RenderMenuFrame(_frameCounter);

            RaiseOverlayRender();
            Hexa.NET.ImGui.ImGui.Render();
            ImGuiImplD3D9.RenderDrawData(Hexa.NET.ImGui.ImGui.GetDrawData());
        }

        public override void OnLostDevice()
        {
            if (IsInitialized)
            {
                ImGuiImplD3D9.InvalidateDeviceObjects();
            }
        }

        public override void OnResetDevice()
        {
            if (IsInitialized)
            {
                ImGuiImplD3D9.CreateDeviceObjects();
            }
        }

        public override void Dispose()
        {
            if (IsInitialized)
            {
                ImGuiImplD3D9.Shutdown();
                ShutdownImGui();
                IsInitialized = false;
            }
        }
    }
}
