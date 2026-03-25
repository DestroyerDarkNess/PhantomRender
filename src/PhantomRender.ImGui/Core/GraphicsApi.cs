using System;
using System.Runtime.InteropServices;

namespace PhantomRender.ImGui.Core
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

    public static class GraphicsApiDetector
    {
        // This is bootstrap detection only. Once hooks are active, the renderer
        // should be treated as authoritative instead of the loaded-module heuristic.
        public static GraphicsApi DetectRenderer()
        {
            if (IsLoaded(GraphicsApi.Vulkan))
            {
                return GraphicsApi.Vulkan;
            }

            if (IsLoaded(GraphicsApi.DirectX12))
            {
                return GraphicsApi.DirectX12;
            }

            if (IsLoaded(GraphicsApi.DirectX11))
            {
                return GraphicsApi.DirectX11;
            }

            if (IsLoaded(GraphicsApi.DirectX10))
            {
                return GraphicsApi.DirectX10;
            }

            if (IsLoaded(GraphicsApi.DirectX9))
            {
                return GraphicsApi.DirectX9;
            }

            if (IsLoaded(GraphicsApi.OpenGL))
            {
                return GraphicsApi.OpenGL;
            }

            return GraphicsApi.Unknown;
        }

        public static bool TryDetectRenderer(out GraphicsApi api)
        {
            api = DetectRenderer();
            return api != GraphicsApi.Unknown;
        }

        public static bool IsLoaded(GraphicsApi api)
        {
            switch (api)
            {
                case GraphicsApi.DirectX9:
                    return IsModuleLoaded("d3d9.dll");
                case GraphicsApi.DirectX10:
                    return IsModuleLoaded("d3d10.dll")
                        || IsModuleLoaded("d3d10_1.dll")
                        || IsModuleLoaded("d3d10core.dll");
                case GraphicsApi.DirectX11:
                    return IsModuleLoaded("d3d11.dll");
                case GraphicsApi.DirectX12:
                    return IsModuleLoaded("d3d12.dll");
                case GraphicsApi.OpenGL:
                    return IsModuleLoaded("opengl32.dll");
                case GraphicsApi.Vulkan:
                    return IsModuleLoaded("vulkan-1.dll");
                default:
                    return false;
            }
        }

        private static bool IsModuleLoaded(string moduleName)
        {
            return GetModuleHandleW(moduleName) != IntPtr.Zero;
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr GetModuleHandleW(string lpModuleName);
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
