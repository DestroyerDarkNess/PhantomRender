using System;
using Hexa.NET.ImGui;
using PhantomRender.ImGui.Core;

namespace PhantomRender.ImGui.Core.Renderers
{
    public interface IOverlayRenderer : IDisposable
    {
        GraphicsApi GraphicsApi { get; }
        bool IsInitialized { get; }
        ImGuiContextPtr Context { get; }
        ImGuiIOPtr IO { get; }

        event Action OnOverlayRender;

        bool Initialize(nint device, nint windowHandle);
        nint CreateExternalWindow(ExternalOverlay overlay);
        void NewFrame();
        void Render();
        void OnLostDevice();
        void OnResetDevice();
    }
}
