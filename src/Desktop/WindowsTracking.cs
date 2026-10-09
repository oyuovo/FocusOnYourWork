using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace FocusIsland.Desktop
{
    public sealed class WindowTarget
    {
        public IntPtr Handle { get; set; }
        public int ProcessId { get; set; }
        public long Started { get; set; }
        public string Name { get; set; }
        public string Title { get; set; }
        public override string ToString() { return Name + " · " + Title; }
    }

    public static class WindowsTracking
    {
        public delegate bool EnumCallback(IntPtr hwnd, IntPtr arg);
        public delegate void WinEventCallback(IntPtr hook, uint type, IntPtr window, int obj, int child, uint thread, uint time);
        [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] private static extern bool EnumWindows(EnumCallback callback, IntPtr arg);
        [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hwnd);
        [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr hwnd);
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out int pid);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr hwnd, StringBuilder text, int max);
        [DllImport("user32.dll")] public static extern IntPtr SetWinEventHook(uint min, uint max, IntPtr module, WinEventCallback callback, uint pid, uint thread, uint flags);
        [DllImport("user32.dll")] public static extern bool UnhookWinEvent(IntPtr hook);
        public static WindowTarget Describe(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero || !IsWindow(hwnd)) return null;
            int pid; GetWindowThreadProcessId(hwnd, out pid);
            try
            {
                using (Process process = Process.GetProcessById(pid))
                {
                    var title = new StringBuilder(512); GetWindowText(hwnd, title, title.Capacity);
                    return new WindowTarget { Handle = hwnd, ProcessId = pid, Started = process.StartTime.ToUniversalTime().Ticks,
                        Name = process.ProcessName, Title = title.ToString() };
                }
            }
            catch (ArgumentException) { return null; }
            catch (InvalidOperationException) { return null; }
            catch (System.ComponentModel.Win32Exception) { return null; }
        }
        public static List<WindowTarget> List()
        {
            var result = new List<WindowTarget>(); int own = Process.GetCurrentProcess().Id;
            EnumWindows(delegate(IntPtr hwnd, IntPtr unused) {
                if (!IsWindowVisible(hwnd)) return true;
                WindowTarget target = Describe(hwnd);
                if (target != null && target.ProcessId != own && !String.IsNullOrWhiteSpace(target.Title) && target.Name != "ApplicationFrameHost") result.Add(target);
                return true;
            }, IntPtr.Zero);
            return result;
        }
        public static bool Matches(WindowTarget selected, WindowTarget actual)
        {
            return selected != null && actual != null && selected.Handle == actual.Handle && selected.ProcessId == actual.ProcessId && selected.Started == actual.Started;
        }
    }
}
