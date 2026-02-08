using System;
using System.Runtime.InteropServices;

namespace PhantomRender.Core.Native
{
    public static class NativeWindowHelper
    {
        public static IntPtr CreateDummyWindow()
        {
            // Register class
            var wndClass = new WNDCLASSEX
            {
                cbSize = (uint)Marshal.SizeOf(typeof(WNDCLASSEX)),
                style = 0,
                lpfnWndProc = FileWndProc,
                cbClsExtra = 0,
                cbWndExtra = 0,
                hInstance = GetModuleHandle(null),
                hIcon = IntPtr.Zero,
                hCursor = IntPtr.Zero,
                hbrBackground = IntPtr.Zero,
                lpszMenuName = null,
                lpszClassName = "PhantomRenderDummyClass_" + Guid.NewGuid().ToString("N")
            };

            if (RegisterClassEx(ref wndClass) == 0)
                return IntPtr.Zero;

            // Create window
            return CreateWindowEx(
                0,
                wndClass.lpszClassName,
                "PhantomRender Dummy",
                0, // WS_OVERLAPPED
                0, 0, 100, 100,
                IntPtr.Zero,
                IntPtr.Zero,
                wndClass.hInstance,
                IntPtr.Zero);
        }

        public static void DestroyDummyWindow(IntPtr hWnd)
        {
            if (hWnd != IntPtr.Zero)
            {
                DestroyWindow(hWnd);
                // Unregister class? We used a unique name, maybe needed if we create many.
                // ideally unregister, but for static helper it's fine.
            }
        }

        private static IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
        {
            return DefWindowProc(hWnd, msg, wParam, lParam);
        }

        private static readonly WndProcDelegate FileWndProc = new WndProcDelegate(WndProc);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)] // ANSI for compatibility
        private struct WNDCLASSEX
        {
            public uint cbSize;
            public uint style;
            [MarshalAs(UnmanagedType.FunctionPtr)]
            public WndProcDelegate lpfnWndProc;
            public int cbClsExtra;
            public int cbWndExtra;
            public IntPtr hInstance;
            public IntPtr hIcon;
            public IntPtr hCursor;
            public IntPtr hbrBackground;
            public string lpszMenuName;
            public string lpszClassName;
            public IntPtr hIconSm;
        }

        private delegate IntPtr WndProcDelegate(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr GetModuleHandle(string lpModuleName);

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Ansi)]
        private static extern ushort RegisterClassEx(ref WNDCLASSEX lpwcx);

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Ansi)]
        private static extern IntPtr CreateWindowEx(
            uint dwExStyle,
            string lpClassName,
            string lpWindowName,
            uint dwStyle,
            int x,
            int y,
            int nWidth,
            int nHeight,
            IntPtr hWndParent,
            IntPtr hMenu,
            IntPtr hInstance,
            IntPtr lpParam);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool DestroyWindow(IntPtr hWnd);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr DefWindowProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
    }
}
