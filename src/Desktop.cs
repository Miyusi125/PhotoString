using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Forms = System.Windows.Forms;

namespace PhotoString
{
    internal static class Native
    {
        internal delegate bool EnumCallback(IntPtr hwnd, IntPtr parameter);
        [DllImport("user32.dll")] internal static extern bool EnumWindows(EnumCallback callback, IntPtr parameter);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern IntPtr FindWindowEx(IntPtr parent, IntPtr child, string name, string title);
        [DllImport("user32.dll", SetLastError = true)] internal static extern IntPtr SetParent(IntPtr child, IntPtr parent);
        [DllImport("user32.dll")] internal static extern IntPtr GetParent(IntPtr child);
        [DllImport("user32.dll")] internal static extern IntPtr GetWindow(IntPtr hwnd, uint command);
        [DllImport("user32.dll")] internal static extern bool IsWindow(IntPtr hwnd);
        [DllImport("user32.dll")] internal static extern bool IsWindowVisible(IntPtr hwnd);
        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] internal static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);
        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)] internal static extern IntPtr SetWindowLongPtr(IntPtr hwnd, int index, IntPtr value);
        [DllImport("user32.dll", SetLastError = true)] internal static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int width, int height, uint flags);
        [DllImport("user32.dll")] internal static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);
        [DllImport("user32.dll")] internal static extern bool ScreenToClient(IntPtr hwnd, ref POINT point);
        [DllImport("user32.dll")] internal static extern bool RegisterHotKey(IntPtr hwnd, int id, uint modifiers, uint key);
        [DllImport("user32.dll")] internal static extern bool UnregisterHotKey(IntPtr hwnd, int id);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern int GetClassName(IntPtr hwnd, StringBuilder result, int count);
        [DllImport("user32.dll", SetLastError = true)] internal static extern bool PrintWindow(IntPtr hwnd, IntPtr hdc, uint flags);
        [StructLayout(LayoutKind.Sequential)] internal struct POINT { internal int X, Y; }
        [StructLayout(LayoutKind.Sequential)] internal struct RECT { internal int Left, Top, Right, Bottom; }
        internal static IntPtr FindDesktopHost()
        {
            IntPtr result = IntPtr.Zero;
            EnumWindows(delegate(IntPtr hwnd, IntPtr parameter)
            {
                if (FindWindowEx(hwnd, IntPtr.Zero, "SHELLDLL_DefView", null) != IntPtr.Zero) { result = hwnd; return false; }
                return true;
            }, IntPtr.Zero);
            return result;
        }
        internal static string ClassName(IntPtr hwnd) { var buffer = new StringBuilder(256); GetClassName(hwnd, buffer, buffer.Capacity); return buffer.ToString(); }
        internal static IntPtr Owner(IntPtr hwnd) { return GetWindow(hwnd, 4); }
    }
    public class DesktopWindow : Window
    {
        public readonly Scene Scene;
        public IntPtr Handle { get { return new WindowInteropHelper(this).Handle; } }
        public bool IsAttached;
        public string LastError;
        private readonly Store store;
        private bool edit;
        public DesktopWindow(Store source)
        {
            store = source; Title = "Photo String Desktop"; ShowInTaskbar = false; ShowActivated = false;
            WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize; AllowsTransparency = true; Background = Brushes.Transparent;
            Width = 1200; Height = 760; Left = -10000; Top = -10000;
            Scene = new Scene(store) { Editable = false, ShowSelection = false, ToolTip = null };
            Content = Scene;
            SourceInitialized += delegate
            {
                var nativeSource = HwndSource.FromHwnd(Handle);
                if (nativeSource != null && nativeSource.CompositionTarget != null) nativeSource.CompositionTarget.RenderMode = RenderMode.SoftwareOnly;
            };
            Loaded += delegate { Attach(); };
        }
        public Forms.Screen TargetScreen
        {
            get
            {
                foreach (var s in Forms.Screen.AllScreens) if (s.DeviceName == store.State.Monitor) return s;
                return Forms.Screen.PrimaryScreen;
            }
        }
        public void Attach()
        {
            var hwnd = Handle; var host = Native.FindDesktopHost();
            if (host == IntPtr.Zero || hwnd == IntPtr.Zero) { IsAttached = false; LastError = "Windows 바탕화면을 찾지 못했습니다. 잠시 후 다시 연결합니다."; return; }
            // Keep WPF's transparent window top-level. Reparenting its render target into
            // Explorer can leave a correctly positioned HWND with an empty visual surface.
            // Desktop ownership + z-order placement keeps it above wallpaper, below apps.
            new WindowInteropHelper(this).Owner = host;
            IsAttached = Native.Owner(hwnd) == host;
            if (!IsAttached) { LastError = "바탕화면 연결 실패 (Windows 오류 " + Marshal.GetLastWin32Error() + "). 관리창에서 배치를 편집할 수 있습니다."; return; }
            LastError = null; ApplyEditStyle(); PositionOnScreen();
        }
        public void PositionOnScreen()
        {
            if (!IsAttached) return;
            var bounds = TargetScreen.Bounds;
            var source = PresentationSource.FromVisual(this); double sx = 1, sy = 1;
            if (source != null && source.CompositionTarget != null) { var matrix = source.CompositionTarget.TransformToDevice; sx = matrix.M11; sy = matrix.M22; }
            Left = bounds.Left / sx; Top = bounds.Top / sy;
            Width = bounds.Width / sx; Height = bounds.Height / sy;
            // Insert immediately above the desktop owner, never above ordinary apps.
            var owner = Native.Owner(Handle);
            var previous = Native.GetWindow(owner, 3); // GW_HWNDPREV
            while (previous == Handle) previous = Native.GetWindow(previous, 3);
            Native.SetWindowPos(Handle, previous, bounds.X, bounds.Y, bounds.Width, bounds.Height, 0x0010 | 0x0200 | (store.State.Visible ? 0x0040U : 0U));
        }
        public void SetEditing(bool editing)
        {
            edit = editing; Scene.Editable = edit; Scene.ShowSelection = edit;
            Scene.ToolTip = edit ? "사진 드래그 · 더블클릭 교체 · 휠 크기 조절 · 우클릭 메뉴" : null;
            ApplyEditStyle(); Scene.InvalidateVisual();
        }
        private void ApplyEditStyle()
        {
            if (Handle == IntPtr.Zero) return;
            long exStyle = Native.GetWindowLongPtr(Handle, -20).ToInt64();
            exStyle |= 0x00000080L | 0x08000000L; // TOOLWINDOW and NOACTIVATE
            if (edit) exStyle &= ~0x00000020L; else exStyle |= 0x00000020L; // click-through when locked
            exStyle &= ~0x00040000L;
            exStyle &= ~0x00000008L; // Never topmost.
            Native.SetWindowLongPtr(Handle, -20, new IntPtr(exStyle));
        }
        public void Refresh() { Scene.InvalidateVisual(); }
    }
}
