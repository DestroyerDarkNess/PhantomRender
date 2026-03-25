
<<<<<<< TODO: Unmerged change from project 'PhantomRender (net48)', Before:
using System;
=======
using PhantomRender;
using PhantomRender.Core;
using PhantomRender.Core;
using PhantomRender.Core.Graphics;
using System;
>>>>>>> After
using System;
using System.Runtime.InteropServices;

namespace PhantomRender.Core
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
            string moduleName = api switch
            {
                GraphicsApi.DirectX9 => "d3d9.dll",
                GraphicsApi.DirectX10 => "d3d10.dll",
                GraphicsApi.DirectX11 => "d3d11.dll",
                GraphicsApi.DirectX12 => "d3d12.dll",
                GraphicsApi.OpenGL => "opengl32.dll",
                GraphicsApi.Vulkan => "vulkan-1.dll",
                _ => null,
            };

            return moduleName != null && GetModuleHandleW(moduleName) != nint.Zero;
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetModuleHandleW", SetLastError = true)]
        private static extern nint GetModuleHandleW(string lpModuleName);
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
