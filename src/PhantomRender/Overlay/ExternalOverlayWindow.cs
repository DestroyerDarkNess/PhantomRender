using System;
using System.Runtime.InteropServices;
using PhantomRender.Core;

namespace PhantomRender.Overlays
{
    public enum ExternalOverlayMode
    {
        Overlay = 0,
        Window = 1,
    }

    public readonly struct OverlayColor : IEquatable<OverlayColor>
    {
        public OverlayColor(byte r, byte g, byte b)
            : this(255, r, g, b)
        {
        }

        public OverlayColor(byte a, byte r, byte g, byte b)
        {
            A = a;
            R = r;
            G = g;
            B = b;
        }

        public byte A { get; }

        public byte R { get; }

        public byte G { get; }

        public byte B { get; }

        public static OverlayColor Black => new OverlayColor(255, 0, 0, 0);

        public static OverlayColor White => new OverlayColor(255, 255, 255, 255);

        public uint ToColorRef()
        {
            return (uint)(R | (G << 8) | (B << 16));
        }

        public uint ToArgb()
        {
            return (uint)((A << 24) | (R << 16) | (G << 8) | B);
        }

        public bool Equals(OverlayColor other)
        {
            return A == other.A && R == other.R && G == other.G && B == other.B;
        }

        public override bool Equals(object obj)
        {
            return obj is OverlayColor other && Equals(other);
        }

        public override int GetHashCode()
        {
            return (int)ToArgb();
        }

        public static bool operator ==(OverlayColor left, OverlayColor right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(OverlayColor left, OverlayColor right)
        {
            return !left.Equals(right);
        }
    }

    public sealed class OverlayWindowEventArgs : EventArgs
    {
        public OverlayWindowEventArgs(nint windowHandle)
        {
            WindowHandle = windowHandle;
        }

        public nint WindowHandle { get; }
    }

    public sealed class ExternalOverlayWindow : IDisposable
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
        private const uint WS_CAPTION = 0x00C00000;
        private const uint WS_THICKFRAME = 0x00040000;
        private const uint WS_MINIMIZEBOX = 0x00020000;
        private const uint WS_MAXIMIZEBOX = 0x00010000;
        private const uint WS_SYSMENU = 0x00080000;
        private const uint WS_BORDER = 0x00800000;
        private const uint WS_EX_TOPMOST = 0x00000008;
        private const uint WS_EX_TOOLWINDOW = 0x00000080;
        private const uint WS_EX_APPWINDOW = 0x00040000;
        private const uint WS_EX_LAYERED = 0x00080000;
        private const uint WS_EX_TRANSPARENT = 0x00000020;
        private const uint WS_EX_NOACTIVATE = 0x08000000;
        private const uint WM_CLOSE = 0x0010;
        private const uint WM_DESTROY = 0x0002;
        private const uint WM_ERASEBKGND = 0x0014;
        private const uint WM_PAINT = 0x000F;
        private const uint PM_REMOVE = 0x0001;
        private const uint MONITOR_DEFAULTTONEAREST = 0x00000002;
        private const uint LWA_COLORKEY = 0x00000001;
        private const uint LWA_ALPHA = 0x00000002;

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
        private string _title = "PhantomRender";
        private ExternalOverlayMode _mode = ExternalOverlayMode.Overlay;
        private bool _borderless;
        private bool _resizable = true;
        private bool _showCaption = true;
        private bool _showMinimizeBox = true;
        private bool _showMaximizeBox = true;
        private bool? _showInTaskbar;
        private double _opacity = 1.0;
        private OverlayColor _backgroundColor = OverlayColor.Black;
        private OverlayColor? _transparencyKey;
        private nint _backgroundBrush;

        public ExternalOverlayWindow(GraphicsApi graphicsApi)
        {
            GraphicsApi = graphicsApi;
            UpdateBackgroundBrush();
        }

        public GraphicsApi GraphicsApi { get; }

        public string Title
        {
            get => _title;
            set
            {
                string nextTitle = string.IsNullOrWhiteSpace(value) ? "PhantomRender" : value;
                if (string.Equals(_title, nextTitle, StringComparison.Ordinal))
                {
                    return;
                }

                _title = nextTitle;
                if (WindowHandle != IntPtr.Zero)
                {
                    SetWindowTextW(WindowHandle, _title);
                }
            }
        }

        public ExternalOverlayMode Mode
        {
            get => _mode;
            set
            {
                if (_mode == value)
                {
                    return;
                }

                _mode = value;
                RefreshWindowStyles();
            }
        }

        public bool ClickThrough
        {
            get => _clickThrough;
            set
            {
                if (_clickThrough == value)
                {
                    return;
                }

                _clickThrough = value;
                RefreshWindowStyles();
            }
        }

        public bool TopMost
        {
            get => _topMost;
            set
            {
                if (_topMost == value)
                {
                    return;
                }

                _topMost = value;
                RefreshWindowStyles();
            }
        }

        public bool Borderless
        {
            get => _borderless;
            set
            {
                if (_borderless == value)
                {
                    return;
                }

                _borderless = value;
                RefreshWindowStyles();
            }
        }

        public bool Resizable
        {
            get => _resizable;
            set
            {
                if (_resizable == value)
                {
                    return;
                }

                _resizable = value;
                RefreshWindowStyles();
            }
        }

        public bool ShowCaption
        {
            get => _showCaption;
            set
            {
                if (_showCaption == value)
                {
                    return;
                }

                _showCaption = value;
                RefreshWindowStyles();
            }
        }

        public bool ShowMinimizeBox
        {
            get => _showMinimizeBox;
            set
            {
                if (_showMinimizeBox == value)
                {
                    return;
                }

                _showMinimizeBox = value;
                RefreshWindowStyles();
            }
        }

        public bool ShowMaximizeBox
        {
            get => _showMaximizeBox;
            set
            {
                if (_showMaximizeBox == value)
                {
                    return;
                }

                _showMaximizeBox = value;
                RefreshWindowStyles();
            }
        }

        public bool ShowInTaskbar
        {
            get => _showInTaskbar ?? _mode == ExternalOverlayMode.Window;
            set
            {
                if (_showInTaskbar.HasValue && _showInTaskbar.Value == value)
                {
                    return;
                }

                _showInTaskbar = value;
                RefreshWindowStyles();
            }
        }

        public double Opacity
        {
            get => _opacity;
            set
            {
                double next = value;
                if (next < 0.0)
                {
                    next = 0.0;
                }
                else if (next > 1.0)
                {
                    next = 1.0;
                }

                if (Math.Abs(_opacity - next) < double.Epsilon)
                {
                    return;
                }

                _opacity = next;
                RefreshWindowStyles();
            }
        }

        public OverlayColor BackgroundColor
        {
            get => _backgroundColor;
            set
            {
                if (_backgroundColor == value)
                {
                    return;
                }

                _backgroundColor = value;
                UpdateBackgroundBrush();
                InvalidateWindow();
            }
        }

        public OverlayColor? TransparencyKey
        {
            get => _transparencyKey;
            set
            {
                if (_transparencyKey.HasValue == value.HasValue &&
                    (!_transparencyKey.HasValue || _transparencyKey.Value == value.Value))
                {
                    return;
                }

                _transparencyKey = value;
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

            return EnsureWindowCreated() != IntPtr.Zero;
        }

        public nint EnsureWindowCreated()
        {
            if (WindowHandle != IntPtr.Zero)
            {
                return WindowHandle;
            }

            _isClosed = false;
            _wndProc = WindowProc;
            _windowClassName = "PhantomRender.ExternalOverlay." + Guid.NewGuid().ToString("N");

            var wndClass = new WNDCLASSEX
            {
                cbSize = (uint)Marshal.SizeOf<WNDCLASSEX>(),
                lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_wndProc),
                hInstance = GetModuleHandleW(null),
                lpszClassName = _windowClassName,
                hbrBackground = IntPtr.Zero,
            };

            _windowClassAtom = RegisterClassExW(ref wndClass);
            if (_windowClassAtom == 0)
            {
                throw new InvalidOperationException($"RegisterClassExW failed. Win32Error={Marshal.GetLastWin32Error()}");
            }

            WindowHandle = CreateWindowExW(
                GetWindowExStyle(),
                _windowClassName,
                _title,
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

        public void DestroyWindow()
        {
            if (WindowHandle == IntPtr.Zero)
            {
                return;
            }

            nint handle = WindowHandle;
            WindowHandle = IntPtr.Zero;
            _visible = false;
            _isClosed = true;

            DestroyWindowNative(handle);

            if (_windowClassAtom != 0)
            {
                UnregisterClassW(_windowClassName, GetModuleHandleW(null));
                _windowClassAtom = 0;
            }

            ReleaseBackgroundBrush();
            WindowDestroyed?.Invoke(this, new OverlayWindowEventArgs(handle));
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            DestroyWindow();
            ReleaseBackgroundBrush();
        }

        private void RefreshWindowStyles()
        {
            if (WindowHandle == IntPtr.Zero)
            {
                return;
            }

            SetWindowLongPtr(WindowHandle, GWL_STYLE, (nint)(long)GetWindowStyle());
            SetWindowLongPtr(WindowHandle, GWL_EXSTYLE, (nint)(long)GetWindowExStyle());
            ApplyLayeredWindowAttributes();
            SetWindowPos(
                WindowHandle,
                _topMost ? new IntPtr(HWND_TOPMOST) : new IntPtr(HWND_NOTOPMOST),
                0,
                0,
                0,
                0,
                SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_FRAMECHANGED | (_visible ? SWP_SHOWWINDOW : 0));
            InvalidateWindow();
        }

        private void ApplyLayeredWindowAttributes()
        {
            if (WindowHandle == IntPtr.Zero || !RequiresLayeredStyle())
            {
                return;
            }

            uint flags = 0;
            uint colorKey = 0;
            byte alpha = (byte)Math.Round(_opacity * 255.0);

            if (_transparencyKey.HasValue)
            {
                flags |= LWA_COLORKEY;
                colorKey = _transparencyKey.Value.ToColorRef();
            }

            if (_opacity < 1.0 || _mode == ExternalOverlayMode.Overlay)
            {
                flags |= LWA_ALPHA;
            }

            if (flags != 0)
            {
                SetLayeredWindowAttributes(WindowHandle, colorKey, alpha, flags);
            }
        }

        private bool RequiresLayeredStyle()
        {
            return _mode == ExternalOverlayMode.Overlay || _transparencyKey.HasValue || _opacity < 1.0;
        }

        private uint GetWindowStyle()
        {
            if (_mode == ExternalOverlayMode.Overlay)
            {
                return WS_POPUP | WS_VISIBLE;
            }

            if (_borderless)
            {
                return WS_POPUP | WS_VISIBLE;
            }

            uint style = WS_VISIBLE;

            if (_showCaption)
            {
                style |= WS_CAPTION;
            }

            if (_resizable)
            {
                style |= WS_THICKFRAME;
            }

            if (_showMinimizeBox)
            {
                style |= WS_MINIMIZEBOX;
            }

            if (_showMaximizeBox)
            {
                style |= WS_MAXIMIZEBOX;
            }

            if (_showCaption || _showMinimizeBox || _showMaximizeBox)
            {
                style |= WS_SYSMENU;
            }

            if (!_showCaption && !_resizable && !_showMinimizeBox && !_showMaximizeBox)
            {
                style |= WS_BORDER;
            }

            return style;
        }

        private uint GetWindowExStyle()
        {
            uint exStyle = 0;

            if (_topMost)
            {
                exStyle |= WS_EX_TOPMOST;
            }

            if (RequiresLayeredStyle())
            {
                exStyle |= WS_EX_LAYERED;
            }

            if (_clickThrough)
            {
                exStyle |= WS_EX_TRANSPARENT;
            }

            if (_mode == ExternalOverlayMode.Overlay)
            {
                exStyle |= WS_EX_NOACTIVATE;
            }

            exStyle |= ShowInTaskbar ? WS_EX_APPWINDOW : WS_EX_TOOLWINDOW;
            return exStyle;
        }

        private void UpdateBackgroundBrush()
        {
            ReleaseBackgroundBrush();
            _backgroundBrush = CreateSolidBrush(_backgroundColor.ToColorRef());
        }

        private void ReleaseBackgroundBrush()
        {
            if (_backgroundBrush == IntPtr.Zero)
            {
                return;
            }

            DeleteObject(_backgroundBrush);
            _backgroundBrush = IntPtr.Zero;
        }

        private void InvalidateWindow()
        {
            if (WindowHandle != IntPtr.Zero)
            {
                InvalidateRect(WindowHandle, IntPtr.Zero, true);
            }
        }

        private nint WindowProc(nint hWnd, uint msg, nint wParam, nint lParam)
        {
            switch (msg)
            {
                case WM_CLOSE:
                    DestroyWindow();
                    return 0;

                case WM_DESTROY:
                    _visible = false;
                    _isClosed = true;
                    return 0;

                case WM_ERASEBKGND:
                    return PaintBackground(hWnd, wParam);

                case WM_PAINT:
                    return PaintWindow(hWnd);
            }

            return DefWindowProcW(hWnd, msg, wParam, lParam);
        }

        private nint PaintBackground(nint hWnd, nint hdc)
        {
            if (hdc == IntPtr.Zero || _backgroundBrush == IntPtr.Zero)
            {
                return 0;
            }

            if (!GetClientRect(hWnd, out RECT rect))
            {
                return 0;
            }

            FillRect(hdc, ref rect, _backgroundBrush);
            return 1;
        }

        private nint PaintWindow(nint hWnd)
        {
            PAINTSTRUCT paint = default;
            nint hdc = BeginPaint(hWnd, ref paint);
            if (hdc != IntPtr.Zero)
            {
                PaintBackground(hWnd, hdc);
                EndPaint(hWnd, ref paint);
            }

            return 0;
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

        [StructLayout(LayoutKind.Sequential)]
        private struct PAINTSTRUCT
        {
            public nint hdc;
            [MarshalAs(UnmanagedType.Bool)]
            public bool fErase;
            public RECT rcPaint;
            [MarshalAs(UnmanagedType.Bool)]
            public bool fRestore;
            [MarshalAs(UnmanagedType.Bool)]
            public bool fIncUpdate;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)]
            public byte[] rgbReserved;
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

        [DllImport("user32.dll", EntryPoint = "DestroyWindow", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool DestroyWindowNative(nint hWnd);

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

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetClientRect(nint hWnd, out RECT lpRect);

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetWindowTextW(nint hWnd, string lpString);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetLayeredWindowAttributes(nint hwnd, uint crKey, byte bAlpha, uint dwFlags);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool InvalidateRect(nint hWnd, nint lpRect, [MarshalAs(UnmanagedType.Bool)] bool bErase);

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

        [DllImport("user32.dll", SetLastError = true)]
        private static extern nint BeginPaint(nint hWnd, ref PAINTSTRUCT lpPaint);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool EndPaint(nint hWnd, ref PAINTSTRUCT lpPaint);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern int FillRect(nint hDC, [In] ref RECT lprc, nint hbr);

        [DllImport("gdi32.dll", SetLastError = true)]
        private static extern nint CreateSolidBrush(uint color);

        [DllImport("gdi32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool DeleteObject(nint hObject);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetModuleHandleW")]
        private static extern nint GetModuleHandleW(string lpModuleName);
    }
}
