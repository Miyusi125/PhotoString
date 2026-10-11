using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace PhotoString
{
    public static class DesktopDiagnostics
    {
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint id);
        [DllImport("user32.dll")] private static extern IntPtr GetWindow(IntPtr hwnd, uint command);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr hwnd, StringBuilder text, int count);
        [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(IntPtr hwnd, int attribute, out int value, int count);
        private static readonly List<string> output = new List<string>();
        public static int Run(string folder)
        {
            Directory.CreateDirectory(folder);
            output.Add("Desktop host: " + Native.FindDesktopHost().ToInt64().ToString("X"));
            var windows = new List<IntPtr>(); Native.EnumWindows(delegate(IntPtr h, IntPtr p) { windows.Add(h); return true; }, IntPtr.Zero);
            foreach (var window in windows)
            {
                var name = Native.ClassName(window); var process = ProcessName(window);
                if (name == "Progman" || name == "WorkerW" || process.StartsWith("PhotoString", StringComparison.OrdinalIgnoreCase) || process.StartsWith("wallpaper", StringComparison.OrdinalIgnoreCase)) Describe(window, 0);
            }
            File.WriteAllLines(Path.Combine(folder, "desktop-diagnostics.txt"), output.ToArray(), Encoding.UTF8); return 0;
        }
        private static string ProcessName(IntPtr hwnd)
        {
            uint id; GetWindowThreadProcessId(hwnd, out id); try { return Process.GetProcessById((int)id).ProcessName; } catch { return "unknown"; }
        }
        private static void Describe(IntPtr hwnd, int depth)
        {
            Native.RECT rect; Native.GetWindowRect(hwnd, out rect); int cloak; DwmGetWindowAttribute(hwnd, 14, out cloak, 4);
            uint id; GetWindowThreadProcessId(hwnd, out id);
            string title = "";
            if (ProcessName(hwnd).StartsWith("PhotoString", StringComparison.OrdinalIgnoreCase)) { var buffer = new StringBuilder(256); GetWindowText(hwnd, buffer, buffer.Capacity); title = " title=" + buffer; }
            output.Add(new string(' ', depth * 2) + hwnd.ToInt64().ToString("X") + " " + Native.ClassName(hwnd) + " process=" + ProcessName(hwnd) + " pid=" + id + " visible=" + Native.IsWindowVisible(hwnd) + " cloak=" + cloak + " rect=" + rect.Left + "," + rect.Top + "," + rect.Right + "," + rect.Bottom + " style=" + Native.GetWindowLongPtr(hwnd, -16).ToInt64().ToString("X") + " ex=" + Native.GetWindowLongPtr(hwnd, -20).ToInt64().ToString("X") + title);
            if (depth >= 3) return;
            for (var child = GetWindow(hwnd, 5); child != IntPtr.Zero; child = GetWindow(child, 2)) Describe(child, depth + 1);
        }
    }
}
