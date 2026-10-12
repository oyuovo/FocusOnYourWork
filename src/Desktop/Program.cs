using System;
using System.IO;
using System.Diagnostics;
using System.Reflection;
using System.Security.Principal;
using System.Windows;
using System.Windows.Markup;
using System.Windows.Media.Imaging;
using FocusOnYourWork.Core;

namespace FocusOnYourWork.Desktop
{
    internal static class Program
    {
        [STAThread]
        public static int Main()
        {
            using (var singleton = new SingleInstance(WindowsIdentity.GetCurrent().User.Value))
            {
                if (!singleton.IsPrimary) {
                    AllowExistingForeground();
                    if (!singleton.ActivateExisting()) MessageBox.Show("专注宗门已经在运行，请从任务栏托盘打开。", "专注宗门");
                    return 0;
                }
                try
                {
                    string directory = Environment.GetEnvironmentVariable("FOCUS_ISLAND_DATA_DIR");
                    if (String.IsNullOrEmpty(directory)) directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FocusIsland");
                    var store = new SaveStore(directory); SaveData save = store.Load();
                    string notice = store.Warning;
                    if (save.Pending != null)
                    {
                        SessionRecord recovered = null;
                        SaveStore.Update(save, delegate(SaveData next) { recovered = Game.Settle(next, next.Pending, true); next.Pending = null; }, store.Save);
                        if (recovered != null) notice = "上次专注意外中断，已恢复落盘的 " + Rules.Materials(recovered.RewardUnits) + " 材料。关闭期间未计时。";
                    }
                    var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                    using (Stream style = File.OpenRead(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Theme.xaml"))) app.Resources = (ResourceDictionary)XamlReader.Load(style);
                    Window window;
                    using (Stream xaml = Assembly.GetExecutingAssembly().GetManifestResourceStream("MainWindow.xaml")) window = (Window)XamlReader.Load(xaml);
                    using (Stream icon = Assembly.GetExecutingAssembly().GetManifestResourceStream("App.ico")) window.Icon = BitmapFrame.Create(icon, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
                    app.MainWindow = window;
                    using (var controller = new MainController(window, store, save, directory, Environment.GetEnvironmentVariable("FOCUS_ISLAND_PIPE_NAME")))
                    {
                        singleton.Listen(delegate {
                            if (!window.Dispatcher.HasShutdownStarted) window.Dispatcher.BeginInvoke(new Action(controller.ShowWindow));
                        });
                        if (notice != null) controller.Message(notice);
                        window.Show(); return app.Run();
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show("专注宗门无法启动：\n" + ex.Message + "\n\n已有存档不会被重置。", "专注宗门", MessageBoxButton.OK, MessageBoxImage.Error);
                    return 1;
                }
            }
        }

        private static void AllowExistingForeground()
        {
            using (var current = Process.GetCurrentProcess()) {
                foreach (var process in Process.GetProcessesByName(current.ProcessName)) {
                    using (process) {
                        if (process.Id != current.Id && process.SessionId == current.SessionId) AllowSetForegroundWindow(process.Id);
                    }
                }
            }
        }
        [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool AllowSetForegroundWindow(int processId);
    }
}
