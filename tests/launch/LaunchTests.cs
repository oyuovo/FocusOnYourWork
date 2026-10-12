using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

internal static class LaunchTests
{
    private static int passed;
    private static void Check(bool condition, string message)
    { if (!condition) throw new Exception(message); passed++; Console.WriteLine("PASS " + message); }
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
    private static bool Wait(Func<bool> condition, int milliseconds)
    {
        var clock = Stopwatch.StartNew();
        do { if (condition()) return true; Thread.Sleep(100); } while (clock.ElapsedMilliseconds < milliseconds);
        return false;
    }
    private static Process Launch(string executable, string data, string pipe)
    {
        var start = new ProcessStartInfo(executable) { UseShellExecute = false, WorkingDirectory = Path.GetTempPath(), WindowStyle = ProcessWindowStyle.Hidden };
        start.EnvironmentVariables["FOCUS_ISLAND_DATA_DIR"] = data;
        start.EnvironmentVariables["FOCUS_ISLAND_PIPE_NAME"] = pipe;
        return Process.Start(start);
    }
    public static int Main(string[] args)
    {
        Process first = null, second = null, restarted = null;
        try {
            string exe = Path.GetFullPath(args[0]), data = Path.GetFullPath(args[1]);
            Check(Process.GetProcessesByName("FocusOnYourWork").Length == 0 && Process.GetProcessesByName("FocusIsland").Length == 0, "no user client is interrupted");
            Check(!Directory.Exists(data), "startup test uses a new isolated save directory");
            using (var file = new BinaryReader(File.OpenRead(exe))) {
                file.BaseStream.Position = 0x3c; int pe = file.ReadInt32();
                file.BaseStream.Position = pe + 24 + 68;
                Check(file.ReadUInt16() == 2, "packaged executable is a Windows GUI app without a console subsystem");
            }
            var version = FileVersionInfo.GetVersionInfo(exe);
            Check(version.ProductName == "FocusOnYourWork" && version.FileDescription.Contains("专注宗门"), "exe exposes product name and description");
            using (var icon = Icon.ExtractAssociatedIcon(exe)) Check(icon != null, "exe contains a Windows shell icon");
            string pipe = "FocusOnYourWork.LaunchTests." + Guid.NewGuid().ToString("N");
            first = Launch(exe, data, pipe);
            IntPtr window = IntPtr.Zero;
            Check(Wait(delegate { first.Refresh(); window = first.MainWindowHandle; return !first.HasExited && window != IntPtr.Zero; }, 15000), "portable exe starts from an unrelated working directory");
            Check(first.ProcessName == "FocusOnYourWork" && first.MainWindowTitle.Contains("专注宗门"), "real process and window carry the product identity");
            PostMessage(window, 0x0010, IntPtr.Zero, IntPtr.Zero); // Close button follows the real WPF hide-to-tray path.
            Check(Wait(delegate { return !IsWindowVisible(window); }, 3000), "isolated client can be hidden before another launch");
            second = Launch(exe, data, pipe);
            Check(second.WaitForExit(5000) && second.ExitCode == 0, "second launch exits successfully without starting another client");
            Check(Wait(delegate { return IsWindowVisible(window); }, 5000), "second launch restores the existing hidden window");
            Check(!first.HasExited && Process.GetProcessesByName("FocusOnYourWork").Length == 1, "one client remains after activation");
            // Only terminate processes spawned above, with isolated data and no focus session.
            first.Kill(); first.WaitForExit();
            restarted = Launch(exe, data, pipe);
            Check(Wait(delegate { restarted.Refresh(); return !restarted.HasExited && restarted.MainWindowHandle != IntPtr.Zero; }, 15000), "client restarts after an abandoned instance mutex");
            Console.WriteLine("RESULT: " + passed + " launch checks passed"); return 0;
        } catch (Exception ex) { Console.WriteLine("FAIL " + ex); return 1; }
        finally {
            foreach (var process in new [] { second, restarted, first }) {
                if (process == null) continue;
                if (!process.HasExited) { process.Kill(); process.WaitForExit(); }
                process.Dispose();
            }
        }
    }
}
