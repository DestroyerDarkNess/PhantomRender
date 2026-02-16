namespace PhantomRender.ImGui.Renderers
{
    public enum GraphicsApi
    {
        Unknown = 0,
        DirectX9 = 1,
        DirectX10 = 2,
        DirectX11 = 3,
        DirectX12 = 4,
        OpenGL = 5,
        Vulkan = 6,
    }

    public static class GraphicsApiExtensions
    {
        public static string ToShortName(this GraphicsApi api)
        {
            return api switch
            {
                GraphicsApi.DirectX9 => "DX9",
                GraphicsApi.DirectX10 => "DX10",
                GraphicsApi.DirectX11 => "DX11",
                GraphicsApi.DirectX12 => "DX12",
                GraphicsApi.OpenGL => "OpenGL",
                GraphicsApi.Vulkan => "Vulkan",
                _ => "Unknown",
            };
        }

        public static string ToDisplayName(this GraphicsApi api)
        {
            return api switch
            {
                GraphicsApi.DirectX9 => "DirectX 9",
                GraphicsApi.DirectX10 => "DirectX 10",
                GraphicsApi.DirectX11 => "DirectX 11",
                GraphicsApi.DirectX12 => "DirectX 12",
                GraphicsApi.OpenGL => "OpenGL",
                GraphicsApi.Vulkan => "Vulkan",
                _ => "Unknown",
            };
        }
    }
}

