using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using FocusIsland.Core;
using Forms = System.Windows.Forms;

namespace FocusIsland.Desktop
{
    public sealed class MainController : IDisposable
    {
        private readonly Window window;
        private readonly SaveStore store;
        private readonly SaveData save;
        private readonly BrowserTracker browser = new BrowserTracker();
        private readonly Stopwatch clock = Stopwatch.StartNew();
        private readonly DispatcherTimer timer = new DispatcherTimer();
        private readonly Forms.NotifyIcon tray;
        private readonly BrowserBridge bridge;
        private readonly IslandView island;
        private readonly WindowsTracking.WinEventCallback foregroundCallback;
        private IntPtr foregroundHook;
        private SessionEngine session;
        private WindowTarget selectedWindow;
        private bool selectedBrowser;
        private long lastTick;
        private long lastSave;
        private bool quitting;
        private bool tickRunning;
        private int moveFrom = -1;
        private int lastDistractions;
        private readonly string dataDirectory;
        private Icon trayIcon;

        private T Get<T>(string name) where T : class { return (T)window.FindName(name); }
        private void Click(string name, Action action) { Get<Button>(name).Click += delegate { Guard(action); }; }
        private string Kind { get { return (string)((ComboBoxItem)Get<ComboBox>("BuildingKind").SelectedItem).Tag; } }
        private string ColorChoice { get { return (string)((ComboBoxItem)Get<ComboBox>("BuildingColor").SelectedItem).Tag; } }

        public MainController(Window window, SaveStore store, SaveData save, string dataDirectory)
        {
            this.window = window; this.store = store; this.save = save; this.dataDirectory = dataDirectory;
            island = new IslandView { Save = save, SelectedPlot = 0, CanEdit = true, PreviewKind = "tree", PreviewColor = "sage" };
            Get<ContentControl>("IslandHost").Content = island;
            Get<TextBox>("IslandName").Text = save.IslandName;
            Get<TextBox>("IslandName").LostKeyboardFocus += delegate { Guard(delegate {
                string name = Get<TextBox>("IslandName").Text.Trim(); if (name.Length == 0) name = "我的专注岛";
                if (name == save.IslandName) return;
                try { Commit(delegate(SaveData next) { next.IslandName = name; }); }
                finally { Get<TextBox>("IslandName").Text = save.IslandName; }
            }); };
            Click("RefreshWindows", RefreshWindows);
            Click("StartButton", Start);
            Click("PauseButton", PauseResume);
            Click("HideButton", delegate { window.Hide(); });
            Click("EndButton", Finish);
            Click("BuildButton", delegate { Commit(delegate(SaveData next) { Game.Build(next, island.SelectedPlot, Kind, ColorChoice); }); RenderIsland(); RenderSummary(); Message("新风景已保存。下一段专注，让小岛继续生长。"); });
            Click("RecolorButton", delegate {
                if (session != null) return;
                Building b = save.Buildings.FirstOrDefault(x => x.Plot == island.SelectedPlot);
                if (b == null) { Message("先选择一处已有建筑。"); return; }
                Commit(delegate(SaveData next) { next.Buildings.First(x => x.Plot == island.SelectedPlot).Color = ColorChoice; });
                RenderIsland(); Message("配色已保存，换色不消耗材料。");
            });
            Click("MoveButton", delegate {
                if (moveFrom >= 0) { moveFrom = -1; Message("已取消移动。"); }
                else if (save.Buildings.Any(x => x.Plot == island.SelectedPlot)) { moveFrom = island.SelectedPlot; Message("点击一个空地块完成移动，再点“移动建筑”可取消。"); }
                else Message("先选择一处已有建筑。");
            });
            Click("BrowserHelp", ShowBrowserHelp);
            Get<ComboBox>("BuildingKind").SelectionChanged += delegate { RenderIsland(); };
            Get<ComboBox>("BuildingColor").SelectionChanged += delegate { RenderIsland(); };
            island.PlotClicked += delegate(int plot) { Guard(delegate {
                if (moveFrom >= 0) { Commit(delegate(SaveData next) { Game.Move(next, moveFrom, plot); }); moveFrom = -1; Message("位置已保存，移动不消耗材料。"); }
                island.SelectedPlot = plot; RenderIsland();
            }); };

            trayIcon = MakeIcon();
            tray = new Forms.NotifyIcon { Icon = trayIcon, Visible = true, Text = "专注岛 · 准备好时开始" };
            tray.DoubleClick += delegate { window.Dispatcher.Invoke(new Action(ShowWindow)); };
            var menu = new Forms.ContextMenuStrip();
            menu.Items.Add("打开专注岛", null, delegate { ShowWindow(); });
            menu.Items.Add("暂停 / 继续", null, delegate { Guard(PauseResume); });
            menu.Items.Add("结束并保存", null, delegate { Guard(Finish); });
            menu.Items.Add(new Forms.ToolStripSeparator());
            menu.Items.Add("打开存档文件夹", null, delegate { Guard(delegate { Process.Start(new ProcessStartInfo(dataDirectory) { UseShellExecute = true }); }); });
            menu.Items.Add("退出专注岛", null, delegate { Guard(Exit); });
            tray.ContextMenuStrip = menu;
            timer.Interval = TimeSpan.FromMilliseconds(500);
            timer.Tick += delegate { Tick(); };
            foregroundCallback = delegate { if (session != null && !quitting) window.Dispatcher.BeginInvoke(new Action(Tick)); };
            bridge = new BrowserBridge(HandleBrowser);
            SystemEvents.SessionSwitch += SessionSwitch;
            SystemEvents.PowerModeChanged += PowerMode;
            window.Closing += delegate(object sender, System.ComponentModel.CancelEventArgs e) { if (!quitting) { e.Cancel = true; window.Hide(); } };
            window.IsVisibleChanged += delegate { if (window.IsVisible) { RenderSummary(); RenderSession(); } };
            RefreshWindows(); RenderSummary(); RenderIsland(); RenderSession();
        }

        private static Icon MakeIcon()
        {
            using (var bitmap = new Bitmap(32, 32))
            {
                using (var g = Graphics.FromImage(bitmap))
                {
                    g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                    g.Clear(System.Drawing.Color.Transparent);
                    using (var b = new SolidBrush(System.Drawing.Color.FromArgb(41, 77, 64))) g.FillEllipse(b, 1, 1, 30, 30);
                    using (var b = new SolidBrush(System.Drawing.Color.FromArgb(219, 225, 186))) {
                        g.FillEllipse(b, 5, 19, 22, 7); g.FillPolygon(b, new [] { new System.Drawing.Point(10, 19), new System.Drawing.Point(16, 8), new System.Drawing.Point(22, 19) });
                    }
                }
                IntPtr handle = bitmap.GetHicon();
                try { using (Icon borrowed = Icon.FromHandle(handle)) return (Icon)borrowed.Clone(); }
                finally { DestroyIcon(handle); }
            }
        }
        [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool DestroyIcon(IntPtr handle);

        private void RefreshWindows()
        {
            var combo = Get<ComboBox>("WindowChoices"); var previous = combo.SelectedItem as WindowTarget;
            var windows = WindowsTracking.List(); combo.ItemsSource = windows;
            combo.SelectedItem = previous == null ? windows.FirstOrDefault(x => x.Name.Equals("Code", StringComparison.OrdinalIgnoreCase)) : windows.FirstOrDefault(x => x.Handle == previous.Handle);
            if (combo.SelectedItem == null && windows.Count > 0) combo.SelectedIndex = 0;
        }
        private void Start()
        {
            int minutes;
            if (!Int32.TryParse(Get<ComboBox>("Minutes").Text, out minutes)) throw new ArgumentException("请输入 5–120 之间的整数分钟。");
            selectedWindow = Get<CheckBox>("UseWindow").IsChecked == true ? Get<ComboBox>("WindowChoices").SelectedItem as WindowTarget : null;
            selectedBrowser = Get<CheckBox>("UseBrowser").IsChecked == true;
            if (selectedBrowser && !browser.Selected) throw new InvalidOperationException("先在 Edge 扩展里选择要阅读的页面。");
            if (selectedWindow == null && !selectedBrowser) throw new InvalidOperationException("请选择一个软件窗口或 Edge 页面。");
            if (selectedBrowser && selectedWindow != null && selectedWindow.Name.Equals("msedge", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("阅读指定页面时，请取消“软件窗口”，避免把整个 Edge 都列为允许范围。");
            var created = new SessionEngine(Get<TextBox>("TaskName").Text, minutes, Get<ComboBox>("Mode").SelectedIndex == 1);
            save.Pending = created.Data;
            try { store.Save(save); } catch { save.Pending = null; throw; }
            session = created; lastTick = clock.ElapsedMilliseconds; lastSave = lastTick; lastDistractions = 0; moveFrom = -1;
            foregroundHook = WindowsTracking.SetWinEventHook(3, 3, IntPtr.Zero, foregroundCallback, 0, 0, 0);
            timer.Start(); RenderSession(); RenderIsland(); window.Hide();
            tray.Text = "专注岛 · 等待目标窗口";
        }
        private Presence Detect()
        {
            var actual = WindowsTracking.Describe(WindowsTracking.GetForegroundWindow());
            if (actual == null) return Presence.Unknown;
            if (WindowsTracking.Matches(selectedWindow, actual)) return Presence.Allowed;
            if (selectedBrowser) return browser.Evaluate(clock.ElapsedMilliseconds, actual.Name.Equals("msedge", StringComparison.OrdinalIgnoreCase));
            return Presence.Away;
        }
        private void Tick()
        {
            if (session == null || tickRunning || quitting) return;
            tickRunning = true;
            try
            {
                long now = clock.ElapsedMilliseconds; long elapsed = now - lastTick; lastTick = now;
                session.Tick(elapsed, Detect());
                if (session.Data.Distractions > lastDistractions)
                {
                    lastDistractions = session.Data.Distractions;
                    tray.ShowBalloonTip(3500, "小岛等你回来", "当前已暂停累计，回来后继续。已有进度会保留。", Forms.ToolTipIcon.Info);
                }
                if (session.Complete) { Finish(); return; }
                if (now - lastSave >= 5000) { store.Save(save); lastSave = now; }
                tray.Text = "专注岛 · " + TimeText(session.Data.EffectiveMs) + (session.Data.Paused ? " · 暂停" : session.Current == Presence.Allowed ? " · 专注中" : " · 等待返回");
                if (window.IsVisible) RenderSession();
            }
            catch (Exception ex)
            {
                if (session != null) session.Pause();
                timer.Stop(); ShowWindow(); Message("计时已暂停，请处理后重新开始：" + ex.Message);
            }
            finally { tickRunning = false; }
        }
        private void PauseResume()
        {
            if (session == null) return;
            if (session.Data.Paused) { session.Resume(); lastTick = clock.ElapsedMilliseconds; timer.Start(); }
            else { Tick(); if (session == null) return; session.Pause(); }
            store.Save(save); RenderSession();
        }
        private void Finish()
        {
            if (session == null) return;
            // Settlement and checkpoint removal are committed together by the same atomic save.
            var current = session;
            SessionRecord result = Game.Settle(save, current.Data, false);
            try { store.Save(save); }
            catch
            {
                if (result != null) { save.History.Remove(result); save.WalletUnits -= result.RewardUnits; save.TotalEffectiveMs -= result.EffectiveMs; }
                save.Pending = current.Data; current.Pause(); throw;
            }
            current.Settled = true; session = null; timer.Stop();
            if (foregroundHook != IntPtr.Zero) { WindowsTracking.UnhookWinEvent(foregroundHook); foregroundHook = IntPtr.Zero; }
            tray.Text = "专注岛 · 成果已保存";
            RenderSummary(); RenderSession(); RenderIsland();
            if (result != null)
            {
                string text = "有效专注 " + (result.EffectiveMs / 60000.0).ToString("0.0") + " 分钟，获得 " + Rules.Materials(result.RewardUnits) + " 材料" + (result.ChallengeWon ? "（含挑战奖励）" : "") + "。";
                Message(text + " 可以稍后回来建设。");
                tray.ShowBalloonTip(4000, "这一段投入，已留在岛上", text, Forms.ToolTipIcon.Info);
            }
        }
        private BrowserReply HandleBrowser(BrowserMessage message)
        {
            if (quitting) return new BrowserReply { Message = "客户端已关闭。" };
            return (BrowserReply)window.Dispatcher.Invoke(new Func<BrowserReply>(delegate {
                try
                {
                    if (message.Type == "select")
                    {
                        if (session != null) return new BrowserReply { Message = "请先结束当前专注，再更换目标页面。", Tracking = selectedBrowser };
                        browser.Select(message, clock.ElapsedMilliseconds);
                        Get<TextBlock>("BrowserLabel").Text = browser.Title;
                        Get<CheckBox>("UseBrowser").IsChecked = true;
                        var w = Get<ComboBox>("WindowChoices").SelectedItem as WindowTarget;
                        if (w != null && w.Name.Equals("msedge", StringComparison.OrdinalIgnoreCase)) Get<CheckBox>("UseWindow").IsChecked = false;
                        Message("Edge 页面已选择。可以只阅读页面，或同时允许一个工作软件窗口。");
                    }
                    else if (message.Type == "status" || message.Type == "disconnect") browser.Update(message, clock.ElapsedMilliseconds);
                    else if (message.Type == "ping") { /* No browsing metadata is needed when idle. */ }
                    else return new BrowserReply { Message = "未知消息类型。" };
                    return new BrowserReply { Ok = true, Tracking = session != null && selectedBrowser && !session.Data.Paused, Selected = browser.Selected, Message = "已连接专注岛" };
                }
                catch (ArgumentException ex) { return new BrowserReply { Message = ex.Message }; }
            }));
        }
        private void RenderSession()
        {
            bool active = session != null;
            Get<StackPanel>("SetupPanel").Visibility = active ? Visibility.Collapsed : Visibility.Visible;
            Get<StackPanel>("SessionPanel").Visibility = active ? Visibility.Visible : Visibility.Collapsed;
            Get<StackPanel>("BuildControls").IsEnabled = !active;
            Get<TextBox>("IslandName").IsReadOnly = active;
            island.CanEdit = !active;
            if (!active) return;
            Get<TextBlock>("ActiveTask").Text = session.Data.Task;
            Get<TextBlock>("TimerText").Text = TimeText(session.Data.EffectiveMs);
            Get<TextBlock>("SessionStatus").Text = session.Data.Paused ? "已暂停 · 返回后可继续" : session.Current == Presence.Allowed ? "正在目标范围内 · 有效时间持续积累" : session.Current == Presence.Unknown ? "等待有效检测数据 · 当前不累计" : "已离开目标范围 · 回来后继续";
            Get<ProgressBar>("SessionProgress").Value = session.Data.EffectiveMs * 100.0 / session.Data.TargetMs;
            Get<TextBlock>("SessionReward").Text = "基础材料 " + Rules.Materials(session.Data.EffectiveMs) + " / 目标 " + (session.Data.TargetMs / 60000) + " 分钟\n" + (session.Data.Challenge ? session.Data.BonusEligible ? "连续挑战进行中 · 达标额外 +20%" : "本轮不计连续加成 · 基础成果仍保留" : "随心模式 · 每一段投入都会保留");
            Get<Button>("PauseButton").Content = session.Data.Paused ? "继续" : "暂停";
        }
        private static string TimeText(long ms) { return String.Format("{0:00}:{1:00}", ms / 60000, (ms / 1000) % 60); }
        private void RenderSummary()
        {
            Get<TextBlock>("Wallet").Text = Rules.Materials(save.WalletUnits);
            Get<TextBlock>("IslandSummary").Text = save.TotalEffectiveMs == 0 ? "从第一棵树开始，慢慢长成喜欢的样子。" : "累计投入 " + (save.TotalEffectiveMs / 60000.0).ToString("0.0") + " 分钟 · 已建 " + save.Buildings.Count + " 处风景";
            var history = Get<StackPanel>("HistoryPanel"); history.Children.Clear();
            Get<TextBlock>("HistoryEmpty").Visibility = save.History.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            foreach (var r in save.History.AsEnumerable().Reverse().Take(4))
            {
                var entry = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };
                entry.Children.Add(new TextBlock { Text = r.Task, TextTrimming = TextTrimming.CharacterEllipsis, ToolTip = r.Task });
                entry.Children.Add(new TextBlock { Text = (r.EffectiveMs / 60000.0).ToString("0.0") + " 分钟  ·  +" + Rules.Materials(r.RewardUnits) + " 材料" + (r.ChallengeWon ? "  ·  挑战完成" : r.Recovered ? "  ·  已恢复" : ""), FontSize = 12, Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(104, 121, 111)), Margin = new Thickness(0, 3, 0, 0) });
                history.Children.Add(entry);
            }
        }
        private void RenderIsland()
        {
            if (island == null) return;
            island.PreviewKind = Kind; island.PreviewColor = ColorChoice; island.CanEdit = session == null; island.InvalidateVisual();
            Building building = save.Buildings.FirstOrDefault(x => x.Plot == island.SelectedPlot);
            Get<TextBlock>("PlotLabel").Text = "地块 " + (island.SelectedPlot + 1) + " · " + (building == null ? "空地 / 建造预览" : Rules.Name(building.Kind));
            Get<Button>("BuildButton").IsEnabled = building == null && session == null;
            Get<Button>("MoveButton").IsEnabled = building != null && session == null;
            Get<Button>("RecolorButton").IsEnabled = building != null && session == null;
            Get<TextBlock>("NextGoal").Text = "下一次改变 · " + Rules.Name(Kind) + " / " + Rules.Materials(Rules.Price(Kind)) + " 材料";
            Get<ProgressBar>("BuildProgress").Value = Math.Min(100, save.WalletUnits * 100.0 / Rules.Price(Kind));
            Get<TextBlock>("Wallet").Text = Rules.Materials(save.WalletUnits);
        }
        public void Message(string message) { Get<TextBlock>("StatusMessage").Text = message; }
        private void Commit(Action<SaveData> change)
        {
            if (session != null) throw new InvalidOperationException("请先结束专注，再调整小岛。");
            SaveStore.Update(save, change, store.Save);
        }
        private void Guard(Action action) { try { action(); } catch (Exception ex) { Message(ex.Message); } }
        private void ShowWindow() { window.Show(); window.WindowState = WindowState.Normal; window.Activate(); RenderSession(); }
        private void ShowBrowserHelp()
        {
            MessageBox.Show(window, "首次连接 Edge：\n\n1. 在 edge://extensions 打开开发人员模式，加载项目中的 extension\\edge 文件夹。\n2. 复制扩展 ID，运行 scripts\\register-edge.ps1 -ExtensionId 该ID。\n3. 在普通网页点扩展图标，再点“选为专注页面”。\n\n设置仅需一次，详细步骤见项目 README。", "连接 Edge 页面", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        private void SessionSwitch(object sender, SessionSwitchEventArgs e)
        {
            if (e.Reason == SessionSwitchReason.SessionLock || e.Reason == SessionSwitchReason.SessionLogoff)
                window.Dispatcher.BeginInvoke(new Action(delegate { Guard(SystemPause); }));
        }
        private void PowerMode(object sender, PowerModeChangedEventArgs e)
        {
            if (e.Mode == PowerModes.Suspend || e.Mode == PowerModes.Resume)
                window.Dispatcher.BeginInvoke(new Action(delegate { Guard(SystemPause); }));
        }
        private void SystemPause() { if (session != null) { session.Pause(); store.Save(save); RenderSession(); } }
        private void Exit()
        {
            if (session != null) Finish(); else store.Save(save);
            quitting = true; Application.Current.Shutdown();
        }
        public void Dispose()
        {
            quitting = true; timer.Stop(); bridge.Dispose();
            if (foregroundHook != IntPtr.Zero) WindowsTracking.UnhookWinEvent(foregroundHook);
            SystemEvents.SessionSwitch -= SessionSwitch; SystemEvents.PowerModeChanged -= PowerMode;
            tray.Visible = false; tray.Dispose(); trayIcon.Dispose();
        }
    }
}
