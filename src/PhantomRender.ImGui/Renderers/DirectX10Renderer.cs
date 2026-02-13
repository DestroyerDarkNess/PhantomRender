using System;
using Hexa.NET.ImGui;
using Hexa.NET.ImGui.Backends.D3D10;
using Hexa.NET.ImGui.Backends.Win32;

namespace PhantomRender.ImGui.Renderers
{
    public sealed class DirectX10Renderer : RendererBase
    {
        public override unsafe bool Initialize(IntPtr device, IntPtr windowHandle)
        {
            if (IsInitialized) return true;

            try
            {
                Console.WriteLine($"[PhantomRender] DirectX10Renderer: Entering Initialize (V1). Device: {device}, Window: {windowHandle}");
                Console.Out.Flush();

                InitializeImGui(windowHandle);
                
                // Synchronize context
                Console.WriteLine("[PhantomRender] DirectX10Renderer: Setting context for D3D10 backend...");
                Console.Out.Flush();
                ImGuiImplD3D10.SetCurrentContext(Context);

                // Initialize D3D10 Backend
                Console.WriteLine("[PhantomRender] DirectX10Renderer: Calling ImGuiImplD3D10.Init...");
                Console.Out.Flush();
                
                if (!ImGuiImplD3D10.Init((ID3D10Device*)device))
                {
                    Console.WriteLine("[PhantomRender] DirectX10Renderer: ImGuiImplD3D10.Init returned FALSE!");
                    Console.Out.Flush();
                    ShutdownImGui();
                    return false;
                }

                IsInitialized = true;
                Console.WriteLine("[PhantomRender] DirectX10Renderer: Initialized Successfully! (V1)");
                Console.Out.Flush();
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[PhantomRender] DirectX10Renderer: Init Error (V1): {ex}");
                Console.Out.Flush();
                return false;
            }
        }

        public override void NewFrame()
        {
            if (!IsInitialized) return;

            Hexa.NET.ImGui.ImGui.SetCurrentContext(Context);
            ImGuiImplD3D10.SetCurrentContext(Context);
            ImGuiImplWin32.SetCurrentContext(Context);

            ImGuiImplD3D10.NewFrame();
            ImGuiImplWin32.NewFrame();
            _inputEmulator?.Update();
            Hexa.NET.ImGui.ImGui.NewFrame();
        }

        public override void Render()
        {
            if (!IsInitialized) return;

            Hexa.NET.ImGui.ImGui.SetCurrentContext(Context);

            // Test window
            Hexa.NET.ImGui.ImGui.SetNextWindowPos(new System.Numerics.Vector2(50, 50), ImGuiCond.FirstUseEver);
            bool showWindow = Hexa.NET.ImGui.ImGui.Begin("PhantomRender DX10");
            if (showWindow)
            {
                Hexa.NET.ImGui.ImGui.Text("Status: Active (DX10)");
                Hexa.NET.ImGui.ImGui.Text($"Window: {_windowHandle}");
            }
            Hexa.NET.ImGui.ImGui.End();

            // Demo window
            Hexa.NET.ImGui.ImGui.ShowDemoWindow();

            RaiseOverlayRender();
            Hexa.NET.ImGui.ImGui.Render();
            ImGuiImplD3D10.RenderDrawData(Hexa.NET.ImGui.ImGui.GetDrawData());
        }

        public override void OnLostDevice()
        {
            if (IsInitialized)
            {
                ImGuiImplD3D10.InvalidateDeviceObjects();
            }
        }

        public override void OnResetDevice()
        {
            if (IsInitialized)
            {
                ImGuiImplD3D10.CreateDeviceObjects();
            }
        }

        public override void Dispose()
        {
            if (IsInitialized)
            {
                ImGuiImplD3D10.Shutdown();
                ShutdownImGui();
                IsInitialized = false;
            }
        }
    }
}
