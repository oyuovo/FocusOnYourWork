using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using FocusOnYourWork.Core;
using FocusOnYourWork.Desktop;

internal static class PresentationTests
{
    private static int passed;
    private static void Check(bool condition, string name)
    { if (!condition) throw new Exception(name); passed++; Console.WriteLine("PASS " + name); }
    private static IEnumerable<T> Find<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is T) yield return (T)root;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            foreach (var child in Find<T>(VisualTreeHelper.GetChild(root, i))) yield return child;
    }
    private static void Pump(int milliseconds)
    {
        var frame = new DispatcherFrame(); var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(milliseconds) };
        timer.Tick += delegate { timer.Stop(); frame.Continue = false; }; timer.Start(); Dispatcher.PushFrame(frame);
    }
    private static void Click(SectPanel panel, string prefix)
    {
        panel.UpdateLayout(); var button = Find<Button>(panel).First(x => (x.Content as string ?? "").TrimStart(' ', '·').StartsWith(prefix, StringComparison.Ordinal));
        if (!button.IsEnabled) throw new Exception("Disabled: " + prefix);
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); panel.UpdateLayout();
    }
    private static void Capture(Window window, string path)
    {
        window.UpdateLayout();
        var content = (FrameworkElement)window.Content;
        var target = new RenderTargetBitmap((int)content.ActualWidth, (int)content.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        target.Render(content); var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(target));
        using (var stream = File.Create(path)) png.Save(stream);
    }
    private static Window OpenWindow()
    {
        using (var source = typeof(MainController).Assembly.GetManifestResourceStream("MainWindow.xaml")) {
            var window = (Window)XamlReader.Load(source); window.WindowStartupLocation = WindowStartupLocation.Manual;
            window.Left = -20000; window.Top = -20000; window.ShowInTaskbar = false; window.ShowActivated = false; return window;
        }
    }
    [STAThread]
    public static int Main(string[] args)
    {
        string output = args[0]; Directory.CreateDirectory(output);
        string data = Path.Combine(output, "data-" + Guid.NewGuid().ToString("N"));
        try {
            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            using (var styles = File.OpenRead(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Theme.xaml"))) app.Resources = (ResourceDictionary)XamlReader.Load(styles);
            var store = new SaveStore(data); var save = new SaveData { SectName = "隔离界面试用" }; var window = OpenWindow();
            using (var controller = new MainController(window, store, save, data, "FocusOnYourWork.Tests." + Guid.NewGuid().ToString("N"))) {
                var panel = (SectPanel)((ContentControl)window.FindName("SectHost")).Content;
                Check(panel.LoadedImageCount == 0, "cold controller does not load scene bitmaps");
                if (args.Length > 1 && (args[1] == "--performance" || args[1] == "--warm-performance")) {
                    RunPerformance(window, panel, save, store, output, args[1] == "--warm-performance"); window.Hide(); return 0;
                }
                window.Show(); Pump(100);
                Capture(window, Path.Combine(output, "01-starter.png"));
                Console.WriteLine("VISIBLE " + window.IsVisible + " PRESENTATION " + panel.PresentationEnabled + " IMAGES " + panel.LoadedImageCount);
                foreach (var t in Find<TextBlock>(panel).Where(x => x.Text.Contains("素材") || x.Text.Contains("清单") || x.Text.Contains("待补"))) Console.WriteLine(t.Text);
                Check(panel.PresentationEnabled && panel.LoadedImageCount == 2, "visible default scene and portrait load");
                var root = (Grid)window.FindName("SceneRoot");
                Check(Math.Abs(panel.ActualWidth - root.ActualWidth) < 1 && Math.Abs(panel.ActualHeight - root.ActualHeight) < 1,
                    "world fills the client area behind all HUD controls");
                Check(panel.OpenPanel == null && ((Border)window.FindName("FocusDrawer")).Visibility == Visibility.Collapsed,
                    "home starts with an unobstructed world and closed drawers");
                Check(!Find<Button>(panel).Any(x => (x.Content as string ?? "").Contains("旧岛")), "retired island page is absent");
                Check(typeof(MainController).Assembly.GetType("FocusOnYourWork.Desktop.IslandView") == null, "retired island renderer is absent from assembly");
                ((Button)window.FindName("OpenFocus")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Check(panel.OpenPanel == "人物" && ((Border)window.FindName("FocusDrawer")).Visibility == Visibility.Collapsed, "first focus entry guides starter selection");
                Capture(window, Path.Combine(output, "01-starter-people.png"));
                Click(panel, "邀此人入宗"); Check(store.Load().Sect.Roster.Single().Id == "sword", "real starter button persists choice");
                ((Button)window.FindName("OpenFocus")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Check(panel.OpenPanel == null && ((Border)window.FindName("FocusDrawer")).IsVisible, "focus drawer replaces management drawer");
                ((TextBox)window.FindName("TaskName")).Text = "保留未开始的任务";
                Click(panel, "人物");
                Check(((Border)window.FindName("FocusDrawer")).Visibility == Visibility.Collapsed, "management navigation closes focus drawer");
                ((Button)window.FindName("OpenFocus")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Check(((TextBox)window.FindName("TaskName")).Text == "保留未开始的任务", "switching panels preserves focus setup");
                ((Button)window.FindName("CloseFocus")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Check(((Border)window.FindName("FocusDrawer")).Visibility == Visibility.Collapsed, "close focus returns to the world");
                SaveStore.Update(save, x => x.WalletUnits = 3000 * Rules.UnitsPerMaterial, store.Save); panel.Refresh();
                Click(panel, "建设"); Click(panel, "扩建洞府"); Check(store.Load().Sect.Capacity == 2, "real construction button commits capacity");
                Click(panel, "人物");
                for (int i = 0; i < 5; i++) { string id = "pity-" + i; SaveStore.Update(save, x => SectGame.Draw(x, id, 0), store.Save); }
                panel.Refresh(); panel.UpdateLayout();
                var selector = Find<ComboBox>(panel).Single(); selector.SelectedIndex = 1;
                Click(panel, "确认保底选择"); Check(store.Load().Sect.Roster.Count == 2 && save.Sect.PendingDrawId == null, "real pity chooser commits selected newcomer");
                SaveStore.Update(save, x => SectGame.Progress(x.Sect, SectGame.AscensionMs, DateTime.UtcNow.ToString("o"), "隔离界面测试"), store.Save);
                panel.Refresh(); Click(panel, "外观");
                var uiButton = Find<Button>(panel).First(x => (x.Content as string) == "界面" && x.IsEnabled);
                uiButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Check(save.Sect.UiTheme == "sword" && save.Sect.SceneTheme == "default", "real theme button leaves scene selection independent");
                Check(((SolidColorBrush)window.FindResource("Accent")).Color == (Color)ColorConverter.ConvertFromString("#376978"), "UI theme reaches window resources");
                Capture(window, Path.Combine(output, "02-appearance.png"));
                SaveStore.Update(save, delegate(SaveData next) {
                    for (int i = 2; i < 6; i++) SectGame.Draw(next, "theme-fixture-" + i, i);
                    SectGame.Progress(next.Sect, SectGame.AscensionMs * 3, DateTime.UtcNow.ToString("o"), "隔离外观样例");
                }, store.Save);
                window.Hide(); window.Show(); Pump(40);
                foreach (var definition in SectGame.Characters) {
                    SaveStore.Update(save, delegate(SaveData next) { SectGame.SetTheme(next, definition.Id, false); SectGame.SetTheme(next, definition.Id, true); }, store.Save);
                    panel.Refresh(); Click(panel, "人物"); panel.UpdateLayout(); Find<ComboBox>(panel).Single().SelectedIndex = Array.FindIndex(SectGame.Characters, x => x.Id == definition.Id);
                    Pump(30); Check(panel.LoadedImageCount == 2, definition.Id + " scene and portrait load together");
                    Capture(window, Path.Combine(output, "theme-" + definition.Id + ".png"));
                }
                Click(panel, "人物"); window.Width = 1080; window.Height = 820; Pump(60);
                Capture(window, Path.Combine(output, "03-minimum-window.png"));
                var drawer = Find<Border>(panel).Single(x => x.Name == "ManagementDrawer");
                var portrait = Find<Image>(panel).Single(x => x.Name == "WorldPortrait");
                var drawerBounds = drawer.TransformToAncestor(root).TransformBounds(new Rect(drawer.RenderSize));
                var portraitBounds = portrait.TransformToAncestor(root).TransformBounds(new Rect(portrait.RenderSize));
                Check(drawerBounds.Right < portraitBounds.Left && drawerBounds.Bottom < root.ActualHeight - 100, "minimum size keeps drawer clear of portrait and navigation");
                ((Button)window.FindName("OpenFocus")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump(40);
                var start = (Button)window.FindName("StartButton");
                start.BringIntoView(); Pump(40);
                Check(start.ActualWidth > 100 && start.ActualHeight > 25, "focus action remains available at minimum window size");
                var focusScroll = Find<ScrollViewer>((Border)window.FindName("FocusDrawer")).First();
                var startBounds = start.TransformToAncestor(focusScroll).TransformBounds(new Rect(start.RenderSize));
                Check(start.IsVisible && startBounds.Top >= 0 && startBounds.Bottom <= focusScroll.ActualHeight + 1, "minimum focus action scrolls fully into the visible drawer");
                Capture(window, Path.Combine(output, "04-focus-minimum.png"));
                ((Button)window.FindName("CloseFocus")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                window.Width = 1600; window.Height = 960; Pump(40);
                Capture(window, Path.Combine(output, "04-wide-home.png"));
                Check(Math.Abs(panel.ActualWidth - root.ActualWidth) < 1, "resizing preserves edge to edge scene coverage");
                window.WindowState = WindowState.Minimized; Pump(60);
                Check(!panel.PresentationEnabled && panel.LoadedImageCount == 0, "minimize clears heavy presentation");
                window.WindowState = WindowState.Normal; Pump(60);
                for (int i = 0; i < 20; i++) {
                    window.Hide(); Check(panel.LoadedImageCount == 0, "hide releases images cycle " + i);
                    window.Show(); Pump(20);
                }
                // Exercise the real controller and OS poller using a deliberately unmatched test target.
                var choices = (ComboBox)window.FindName("WindowChoices"); choices.ItemsSource = new [] {
                    new WindowTarget { Handle = new IntPtr(1), ProcessId = -1, Started = 0, Name = "TestTarget", Title = "隔离检测目标" }
                }; choices.SelectedIndex = 0;
                ((Button)window.FindName("StartButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump(1100);
                Check(save.Pending != null && !window.IsVisible && panel.LoadedImageCount == 0, "start focus unloads art and runs detector");
                Check(save.Pending.EffectiveMs == 0, "unmatched test target receives no credit");
                window.Show(); Pump(40); Check(panel.LoadedImageCount == 0, "viewing timer during focus keeps art unloaded");
                ((Button)window.FindName("OpenFocus")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump(40);
                Check(((StackPanel)window.FindName("SessionPanel")).IsVisible && !Find<Button>(panel).Any(), "active focus shows timer with game navigation unloaded");
                Capture(window, Path.Combine(output, "05-active-focus.png"));
                ((Button)window.FindName("EndButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Check(save.Pending == null && store.Load().History.Count == 1, "controller settles once through real end button");
                window.Hide();
            }
            CheckArt(output);
            Console.WriteLine("RESULT: " + passed + " presentation checks passed"); app.Shutdown(); return 0;
        } catch (Exception ex) { Console.WriteLine("FAIL " + ex); return 1; }
    }
    private static void CheckArt(string output)
    {
        string source = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Art"); var catalog = new ArtCatalog(source);
        Check(catalog.Scene("default") != null && catalog.Portrait("sword") != null, "default assets decode");
        var image = catalog.Portrait("sword"); var converted = new FormatConvertedBitmap(image, PixelFormats.Bgra32, null, 0); byte[] pixel = new byte[4];
        converted.CopyPixels(new Int32Rect(0, 0, 1, 1), pixel, 4, 0); Check(pixel[3] == 0, "portrait has genuine transparent alpha");
        using (File.Open(Path.Combine(source, "portrait-sword.png"), FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { Check(true, "decoded portrait does not lock replaceable file"); }
        string isolated = Path.Combine(output, "art-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(isolated);
        string manifest = File.ReadAllText(Path.Combine(source, "manifest.json"));
        File.WriteAllText(Path.Combine(isolated, "manifest.json"), manifest.Replace("scene-default.png", "../escape.png"));
        var broken = new ArtCatalog(isolated); Check(broken.Warning != null, "unsafe art path falls back without breaking focus UI");
        bool rejected = false; try { broken.Reload(); } catch (InvalidDataException) { rejected = true; }
        Check(rejected, "reload rejects nested paths");
        File.WriteAllText(Path.Combine(isolated, "manifest.json"), manifest);
        File.WriteAllText(Path.Combine(isolated, "scene-default.png"), "invalid-image"); broken.Reload();
        Check(broken.Scene("default") == null && broken.Warning != null, "corrupt image is isolated from timer and save");
        int scenes = SectGame.Characters.Count(x => catalog.HasScene(x.Id));
        int portraits = SectGame.Characters.Count(x => catalog.Portrait(x.Id) != null);
        Check(scenes == 6 && portraits == 6, "all six ascension scenes and portraits ship with this build");
        Console.WriteLine("ASSETS: default scene + " + scenes + "/6 ascension scenes, " + portraits + "/6 portraits (incomplete assets are reported, not passed as complete)");
    }
    [DllImport("psapi.dll", SetLastError = true)]
    private static extern bool QueryWorkingSet(IntPtr process, IntPtr buffer, int size);
    private static double? PrivateWorkingSetMiB(Process process)
    {
        // PSAPI_WORKING_SET_INFORMATION is a pointer-sized count followed by pointer-sized entries.
        // Shared is bit 8 of PSAPI_WORKING_SET_BLOCK; sum resident pages which are not shared.
        for (int size = 1024 * 1024; size <= 16 * 1024 * 1024; size *= 2) {
            IntPtr buffer = Marshal.AllocHGlobal(size);
            try {
                if (!QueryWorkingSet(process.Handle, buffer, size)) continue;
                long count = Marshal.ReadIntPtr(buffer).ToInt64(), privatePages = 0;
                if (count < 0 || count >= size / IntPtr.Size) return null;
                for (int i = 1; i <= count; i++) if ((Marshal.ReadIntPtr(buffer, i * IntPtr.Size).ToInt64() & 0x100) == 0) privatePages++;
                return privatePages * (double)Environment.SystemPageSize / 1048576.0;
            } finally { Marshal.FreeHGlobal(buffer); }
        }
        return null;
    }
    private static void RunPerformance(Window window, SectPanel panel, SaveData save, SaveStore store, string output, bool warmOnly)
    {
        SectGame.ChooseStarter(save, "sword");
        if (warmOnly) {
            save.WalletUnits = 10000 * Rules.UnitsPerMaterial;
            for (int i = 1; i < 6; i++) SectGame.Draw(save, "perf-" + i, i);
            SectGame.Progress(save.Sect, SectGame.AscensionMs * 6, DateTime.UtcNow.ToString("o"), "隔离性能样例");
        }
        var choices = (ComboBox)window.FindName("WindowChoices"); choices.ItemsSource = new [] {
            new WindowTarget { Handle = new IntPtr(1), ProcessId = -1, Name = "TestTarget", Title = "隔离检测目标" }
        }; choices.SelectedIndex = 0;
        foreach (string phase in warmOnly ? new [] { "all-themes-hot" } : new [] { "cold", "hot-20-cycles" }) {
            if (phase != "cold") {
                for (int i = 0; i < 20; i++) {
                    if (warmOnly) { SectGame.SetTheme(save, SectGame.Characters[i % 6].Id, false); SectGame.SetTheme(save, SectGame.Characters[i % 6].Id, true); }
                    window.Show(); Pump(80);
                    if (warmOnly) { panel.OpenPeople(); panel.UpdateLayout(); Find<ComboBox>(panel).Single().SelectedIndex = i % 6; Pump(50); }
                    window.Hide(); Pump(20);
                }
            }
            ((Button)window.FindName("StartButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(save.Pending != null && panel.LoadedImageCount == 0, phase + " running hidden detector");
            Console.WriteLine("PHASE " + phase + " " + DateTime.UtcNow.ToString("o"));
            var rows = new List<string> { "Seconds,CpuPercentOfMachine,WorkingSetMiB,PrivateCommittedMiB,LoadedImages,HandleCount,PrivateWorkingSetMiB" };
            var watch = Stopwatch.StartNew(); using (var process = Process.GetCurrentProcess()) {
                double lastSeconds = 0, lastCpu = process.TotalProcessorTime.TotalSeconds;
                while (watch.Elapsed.TotalSeconds < 600) {
                    Pump(5000); process.Refresh(); double seconds = watch.Elapsed.TotalSeconds, cpu = process.TotalProcessorTime.TotalSeconds;
                    rows.Add(String.Format(System.Globalization.CultureInfo.InvariantCulture, "{0:F3},{1:F5},{2:F2},{3:F2},{4},{5},{6:F2}", seconds,
                        (cpu-lastCpu)/(seconds-lastSeconds)/Environment.ProcessorCount*100, process.WorkingSet64/1048576.0, process.PrivateMemorySize64/1048576.0, panel.LoadedImageCount, process.HandleCount, PrivateWorkingSetMiB(process)));
                    lastSeconds = seconds; lastCpu = cpu;
                    File.WriteAllLines(Path.Combine(output, phase + ".csv"), rows);
                }
            }
            ((Button)window.FindName("EndButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(save.Pending == null && save.History.Last().EffectiveMs == 0, phase + " settles without unproven credit");
        }
    }
}
