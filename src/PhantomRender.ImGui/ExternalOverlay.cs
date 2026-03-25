using System;
using System.Runtime.InteropServices;
using PhantomRender.ImGui.Core;
using PhantomRender.ImGui.Core.Renderers;

namespace PhantomRender.ImGui
{
    public sealed class ExternalOverlay : Overlay
    {
        private const int SW_HIDE = 0;
        private const int SW_SHOW = 5;
        private const int HWND_TOPMOST = -1;
        private const int HWND_NOTOPMOST = -2;
        private const uint SWP_NOMOVE = 0x0002;
        private const uint SWP_NOSIZE = 0x0001;
        private const uint SWP_NOACTIVATE = 0x0010;
        private const uint SWP_FRAMECHANGED = 0x0020;
        private const uint SWP_SHOWWINDOW = 0x0040;
        private const int GWL_STYLE = -16;
        private const int GWL_EXSTYLE = -20;
        private const uint WS_VISIBLE = 0x10000000;
        private const uint WS_POPUP = 0x80000000;
        private const uint WS_OVERLAPPEDWINDOW = 0x00CF0000;
        private const uint WS_EX_TOPMOST = 0x00000008;
        private const uint WS_EX_TOOLWINDOW = 0x00000080;
        private const uint WS_EX_LAYERED = 0x00080000;
        private const uint WS_EX_TRANSPARENT = 0x00000020;
        private const uint WS_EX_NOACTIVATE = 0x08000000;
        private const uint WM_CLOSE = 0x0010;
        private const uint WM_DESTROY = 0x0002;
        private const uint PM_REMOVE = 0x0001;
        private const uint MONITOR_DEFAULTTONEAREST = 0x00000002;

        private readonly IOverlayRenderer _renderer;
        private WndProcDelegate _wndProc;
        private ushort _windowClassAtom;
        private string _windowClassName;
        private bool _disposed;
        private bool _visible;
        private bool _isClosed;
        private bool _clickThrough = true;
        private bool _topMost = true;
        private int _x = 100;
        private int _y = 100;
        private int _width = 1280;
        private int _height = 720;

        public ExternalOverlay(GraphicsApi graphicsApi)
            : this(CreateDefaultRenderer(graphicsApi))
        {
        }

        public ExternalOverlay(RendererBase renderer)
            : base(renderer)
        {
            _renderer = renderer ?? throw new ArgumentNullException(nameof(renderer));
        }

        public IOverlayRenderer Renderer => _renderer;

        public string Title { get; set; } = "PhantomRender";

        public ExternalOverlayMode Mode { get; set; } = ExternalOverlayMode.Overlay;

        public bool ClickThrough
        {
            get => _clickThrough;
            set
            {
                _clickThrough = value;
                RefreshWindowStyles();
            }
        }

        public bool TopMost
        {
            get => _topMost;
            set
            {
                _topMost = value;
                RefreshWindowStyles();
            }
        }

        public bool Visible => _visible;

        public bool IsClosed => _isClosed;

        public bool IsWindowCreated => WindowHandle != IntPtr.Zero;

        public bool IsAttached => AttachedWindowHandle != IntPtr.Zero;

        public bool SupportsExternalWindow => GraphicsApi == GraphicsApi.DirectX9 || GraphicsApi == GraphicsApi.OpenGL;

        public nint WindowHandle { get; private set; }

        public nint AttachedWindowHandle { get; private set; }

        public nint CurrentMonitorHandle
        {
            get
            {
                nint source = AttachedWindowHandle != IntPtr.Zero ? AttachedWindowHandle : WindowHandle;
                return source != IntPtr.Zero ? MonitorFromWindow(source, MONITOR_DEFAULTTONEAREST) : IntPtr.Zero;
            }
        }

        public int X
        {
            get => _x;
            set => SetBounds(value, _y, _width, _height);
        }

        public int Y
        {
            get => _y;
            set => SetBounds(_x, value, _width, _height);
        }

        public int Width
        {
            get => _width;
            set => SetBounds(_x, _y, value, _height);
        }

        public int Height
        {
            get => _height;
            set => SetBounds(_x, _y, _width, value);
        }

        public event EventHandler<OverlayWindowEventArgs> WindowCreated;

        public event EventHandler<OverlayWindowEventArgs> WindowDestroyed;

        public event EventHandler<OverlayWindowEventArgs> AttachedWindowChanged;

        public bool CreateWindow()
        {
            if (!SupportsExternalWindow)
            {
                return false;
            }

            try
            {
                return _renderer.CreateExternalWindow(this) != IntPtr.Zero;
            }
            catch (Exception ex)
            {
                ReportRuntimeError("CreateExternalWindow", ex);
                return false;
            }
        }

        internal nint EnsureWindowCreated()
        {
            if (WindowHandle != IntPtr.Zero)
            {
                return WindowHandle;
            }

            _wndProc = WindowProc;
            _windowClassName = "PhantomRender.ExternalOverlay." + Guid.NewGuid().ToString("N");

            var wndClass = new WNDCLASSEX
            {
                cbSize = (uint)Marshal.SizeOf<WNDCLASSEX>(),
                lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_wndProc),
                hInstance = GetModuleHandleW(null),
                lpszClassName = _windowClassName,
            };

            _windowClassAtom = RegisterClassExW(ref wndClass);
            if (_windowClassAtom == 0)
            {
                throw new InvalidOperationException($"RegisterClassExW failed. Win32Error={Marshal.GetLastWin32Error()}");
            }

            WindowHandle = CreateWindowExW(
                GetWindowExStyle(),
                _windowClassName,
                Title,
                GetWindowStyle(),
                _x,
                _y,
                _width,
                _height,
                IntPtr.Zero,
                IntPtr.Zero,
                wndClass.hInstance,
                IntPtr.Zero);

            if (WindowHandle == IntPtr.Zero)
            {
                ushort atom = _windowClassAtom;
                _windowClassAtom = 0;
                UnregisterClassW(_windowClassName, wndClass.hInstance);
                throw new InvalidOperationException($"CreateWindowExW failed. Win32Error={Marshal.GetLastWin32Error()}");
            }

            RefreshWindowStyles();

            if (_visible)
            {
                ShowWindow(WindowHandle, SW_SHOW);
            }

            WindowCreated?.Invoke(this, new OverlayWindowEventArgs(WindowHandle));
            return WindowHandle;
        }

        public bool Initialize(nint device)
        {
            nint windowHandle = EnsureWindowCreated();
            return _renderer.Initialize(device, windowHandle);
        }

        public void Show()
        {
            _visible = true;
            if (WindowHandle != IntPtr.Zero)
            {
                ShowWindow(WindowHandle, SW_SHOW);
            }
        }

        public void Hide()
        {
            _visible = false;
            if (WindowHandle != IntPtr.Zero)
            {
                ShowWindow(WindowHandle, SW_HIDE);
            }
        }

        public void SetBounds(int x, int y, int width, int height)
        {
            _x = x;
            _y = y;
            _width = Math.Max(1, width);
            _height = Math.Max(1, height);

            if (WindowHandle != IntPtr.Zero)
            {
                SetWindowPos(WindowHandle, IntPtr.Zero, _x, _y, _width, _height, SWP_NOACTIVATE);
            }
        }

        public bool AttachToWindow(nint windowHandle)
        {
            if (windowHandle == IntPtr.Zero || !IsWindow(windowHandle))
            {
                return false;
            }

            AttachedWindowHandle = windowHandle;
            SyncToAttachedWindow();
            AttachedWindowChanged?.Invoke(this, new OverlayWindowEventArgs(windowHandle));
            return true;
        }

        public void DetachFromWindow()
        {
            if (AttachedWindowHandle == IntPtr.Zero)
            {
                return;
            }

            AttachedWindowHandle = IntPtr.Zero;
            AttachedWindowChanged?.Invoke(this, new OverlayWindowEventArgs(IntPtr.Zero));
        }

        public bool SyncToAttachedWindow()
        {
            if (AttachedWindowHandle == IntPtr.Zero)
            {
                return false;
            }

            if (!GetWindowRect(AttachedWindowHandle, out RECT rect))
            {
                return false;
            }

            SetBounds(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top);
            return true;
        }

        public void ProcessEvents()
        {
            while (PeekMessageW(out MSG msg, WindowHandle, 0, 0, PM_REMOVE))
            {
                TranslateMessage(ref msg);
                DispatchMessageW(ref msg);
            }
        }

        public void BeginFrame()
        {
            _renderer.NewFrame();
        }

        public void RenderFrame()
        {
            _renderer.Render();
        }

        public void OnLostDevice()
        {
            _renderer.OnLostDevice();
        }

        public void OnResetDevice()
        {
            _renderer.OnResetDevice();
        }

        public void DestroyWindow()
        {
            if (WindowHandle == IntPtr.Zero)
            {
                return;
            }

            nint handle = WindowHandle;
            WindowHandle = IntPtr.Zero;

            DestroyWindow(handle);

            if (_windowClassAtom != 0)
            {
                UnregisterClassW(_windowClassName, GetModuleHandleW(null));
                _windowClassAtom = 0;
            }

            WindowDestroyed?.Invoke(this, new OverlayWindowEventArgs(handle));
        }

        public override void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            DestroyWindow();
            _renderer.Dispose();
            base.Dispose();
        }

        private void RefreshWindowStyles()
        {
            if (WindowHandle == IntPtr.Zero)
            {
                return;
            }

            SetWindowLongPtr(WindowHandle, GWL_STYLE, (nint)(long)GetWindowStyle());
            SetWindowLongPtr(WindowHandle, GWL_EXSTYLE, (nint)(long)GetWindowExStyle());
            SetWindowPos(
                WindowHandle,
                _topMost ? new IntPtr(HWND_TOPMOST) : new IntPtr(HWND_NOTOPMOST),
                0,
                0,
                0,
                0,
                SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_FRAMECHANGED | (_visible ? SWP_SHOWWINDOW : 0));
        }

        private uint GetWindowStyle()
        {
            return Mode == ExternalOverlayMode.Overlay ? WS_POPUP | WS_VISIBLE : WS_OVERLAPPEDWINDOW | WS_VISIBLE;
        }

        private uint GetWindowExStyle()
        {
            uint exStyle = 0;

            if (_topMost)
            {
                exStyle |= WS_EX_TOPMOST;
            }

            if (Mode == ExternalOverlayMode.Overlay)
            {
                exStyle |= WS_EX_TOOLWINDOW | WS_EX_LAYERED | WS_EX_NOACTIVATE;

                if (_clickThrough)
                {
                    exStyle |= WS_EX_TRANSPARENT;
                }
            }

            return exStyle;
        }

        private nint WindowProc(nint hWnd, uint msg, nint wParam, nint lParam)
        {
            switch (msg)
            {
                case WM_CLOSE:
                    _visible = false;
                    _isClosed = true;
                    DestroyWindow(hWnd);
                    return 0;
                case WM_DESTROY:
                    _visible = false;
                    _isClosed = true;
                    return 0;
            }

            return DefWindowProcW(hWnd, msg, wParam, lParam);
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct WNDCLASSEX
        {
            public uint cbSize;
            public uint style;
            public nint lpfnWndProc;
            public int cbClsExtra;
            public int cbWndExtra;
            public nint hInstance;
            public nint hIcon;
            public nint hCursor;
            public nint hbrBackground;
            public string lpszMenuName;
            public string lpszClassName;
            public nint hIconSm;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT
        {
            public int X;
            public int Y;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MSG
        {
            public nint hwnd;
            public uint message;
            public nuint wParam;
            public nint lParam;
            public uint time;
            public POINT pt;
            public uint lPrivate;
        }

        private delegate nint WndProcDelegate(nint hWnd, uint msg, nint wParam, nint lParam);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "RegisterClassExW", SetLastError = true)]
        private static extern ushort RegisterClassExW(ref WNDCLASSEX lpwcx);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "UnregisterClassW", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool UnregisterClassW(string lpClassName, nint hInstance);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "CreateWindowExW", SetLastError = true)]
        private static extern nint CreateWindowExW(
            uint dwExStyle,
            string lpClassName,
            string lpWindowName,
            uint dwStyle,
            int x,
            int y,
            int nWidth,
            int nHeight,
            nint hWndParent,
            nint hMenu,
            nint hInstance,
            nint lpParam);

        [DllImport("user32.dll", EntryPoint = "DefWindowProcW")]
        private static extern nint DefWindowProcW(nint hWnd, uint msg, nint wParam, nint lParam);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool DestroyWindow(nint hWnd);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool ShowWindow(nint hWnd, int nCmdShow);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetWindowPos(nint hWnd, nint hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);

        [DllImport("user32.dll", SetLastError = true, EntryPoint = "SetWindowLongPtrW")]
        private static extern nint SetWindowLongPtr(nint hWnd, int nIndex, nint dwNewLong);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetWindowRect(nint hWnd, out RECT lpRect);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool IsWindow(nint hWnd);

        [DllImport("user32.dll", EntryPoint = "PeekMessageW")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool PeekMessageW(out MSG lpMsg, nint hWnd, uint wMsgFilterMin, uint wMsgFilterMax, uint wRemoveMsg);

        [DllImport("user32.dll", EntryPoint = "TranslateMessage")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool TranslateMessage(ref MSG lpMsg);

        [DllImport("user32.dll", EntryPoint = "DispatchMessageW")]
        private static extern nint DispatchMessageW(ref MSG lpMsg);

        [DllImport("user32.dll")]
        private static extern nint MonitorFromWindow(nint hwnd, uint dwFlags);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetModuleHandleW")]
        private static extern nint GetModuleHandleW(string lpModuleName);
    }
}
