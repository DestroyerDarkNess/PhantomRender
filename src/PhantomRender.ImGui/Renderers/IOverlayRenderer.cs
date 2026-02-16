using System;
using Hexa.NET.ImGui;

namespace PhantomRender.ImGui.Renderers
{
    public interface IOverlayRenderer : IDisposable
    {
        GraphicsApi GraphicsApi { get; }
        bool IsInitialized { get; }
        ImGuiContextPtr Context { get; }
        ImGuiIOPtr IO { get; }

        event Action OnOverlayRender;

        bool Initialize(IntPtr device, IntPtr windowHandle);
        void NewFrame();
        void Render();
        void OnLostDevice();
        void OnResetDevice();
    }
}
