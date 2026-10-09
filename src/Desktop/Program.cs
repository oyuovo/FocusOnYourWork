using System;
using System.IO;
using System.Reflection;
using System.Security.Principal;
using System.Threading;
using System.Windows;
using System.Windows.Markup;
using FocusIsland.Core;

namespace FocusIsland.Desktop
{
    internal static class Program
    {
        [STAThread]
        public static int Main()
        {
            bool created;
            using (var singleton = new Mutex(true, "Local\\FocusIsland.v1." + WindowsIdentity.GetCurrent().User.Value, out created))
            {
                if (!created) { MessageBox.Show("专注岛已经在运行，请从任务栏托盘打开。", "专注岛"); return 0; }
                try
                {
                    string directory = Environment.GetEnvironmentVariable("FOCUS_ISLAND_DATA_DIR");
                    if (String.IsNullOrEmpty(directory)) directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FocusIsland");
                    var store = new SaveStore(directory); SaveData save = store.Load();
                    string notice = store.Warning;
                    if (save.Pending != null)
                    {
                        var recovered = Game.Settle(save, save.Pending, true); save.Pending = null; store.Save(save);
                        if (recovered != null) notice = "上次专注意外中断，已恢复落盘的 " + Rules.Materials(recovered.RewardUnits) + " 材料。关闭期间未计时。";
                    }
                    var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                    Window window;
                    using (Stream xaml = Assembly.GetExecutingAssembly().GetManifestResourceStream("MainWindow.xaml")) window = (Window)XamlReader.Load(xaml);
                    app.MainWindow = window;
                    using (var controller = new MainController(window, store, save, directory))
                    {
                        if (notice != null) controller.Message(notice);
                        window.Show(); return app.Run();
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show("专注岛无法启动：\n" + ex.Message + "\n\n已有存档不会被重置。", "专注岛", MessageBoxButton.OK, MessageBoxImage.Error);
                    return 1;
                }
            }
        }
    }
}
