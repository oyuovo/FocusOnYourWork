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
using FocusOnYourWork.Core;
using Forms = System.Windows.Forms;

namespace FocusOnYourWork.Desktop
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
        private readonly SectPanel sect;
        private readonly WindowsTracking.WinEventCallback foregroundCallback;
        private IntPtr foregroundHook;
        private SessionEngine session;
        private WindowTarget selectedWindow;
        private bool selectedBrowser;
        private long lastTick;
        private long lastSave;
        private bool quitting;
        private bool tickRunning;
        private int lastDistractions;
        private readonly string dataDirectory;
        private Icon trayIcon;

        private T Get<T>(string name) where T : class { return (T)window.FindName(name); }
        private void Click(string name, Action action) { Get<Button>(name).Click += delegate { Guard(action); }; }

        public MainController(Window window, SaveStore store, SaveData save, string dataDirectory, string isolatedPipeName = null)
        {
            this.window = window; this.store = store; this.save = save; this.dataDirectory = dataDirectory;
            sect = new SectPanel(save, window, delegate(Action<SaveData> change) { Commit(change); RenderSummary(); }, Message);
            Get<ContentControl>("SectHost").Content = sect;
            sect.NavigationChanged += delegate { SetFocusVisible(false); };
            Click("OpenFocus", OpenFocus);
            Click("CloseFocus", delegate { SetFocusVisible(false); });
            window.PreviewKeyDown += delegate(object sender, System.Windows.Input.KeyEventArgs e) {
                if (e.Key != System.Windows.Input.Key.Escape) return;
                SetFocusVisible(false); sect.ClosePanel(); e.Handled = true;
            };
            Get<TextBox>("SectName").Text = save.SectName;
            Get<TextBox>("SectName").LostKeyboardFocus += delegate { Guard(delegate {
                string name = Get<TextBox>("SectName").Text.Trim(); if (name.Length == 0) name = "我的宗门";
                if (name == save.SectName) return;
                try { Commit(delegate(SaveData next) { next.SectName = name; }); RenderSect(); }
                finally { Get<TextBox>("SectName").Text = save.SectName; }
            }); };
            Click("RefreshWindows", RefreshWindows); Click("StartButton", Start);
            Click("PauseButton", PauseResume); Click("EndButton", Finish);
            Click("HideButton", delegate { window.Hide(); }); Click("BrowserHelp", ShowBrowserHelp);
            using (var icon = System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream("App.ico")) trayIcon = new Icon(icon, 32, 32);
            tray = new Forms.NotifyIcon { Icon = trayIcon, Visible = true, Text = "专注宗门 · 准备好时开始" };
            tray.DoubleClick += delegate { window.Dispatcher.Invoke(new Action(ShowWindow)); };
            var menu = new Forms.ContextMenuStrip();
            menu.Items.Add("打开专注宗门", null, delegate { ShowWindow(); });
            menu.Items.Add("暂停 / 继续", null, delegate { Guard(PauseResume); });
            menu.Items.Add("结束并保存", null, delegate { Guard(Finish); });
            menu.Items.Add(new Forms.ToolStripSeparator());
            menu.Items.Add("打开存档文件夹", null, delegate { Guard(delegate { Process.Start(new ProcessStartInfo(dataDirectory) { UseShellExecute = true }); }); });
            menu.Items.Add("退出专注宗门", null, delegate { Guard(Exit); });
            tray.ContextMenuStrip = menu;
            timer.Interval = TimeSpan.FromMilliseconds(500);
            timer.Tick += delegate { Tick(); };
            foregroundCallback = delegate { if (session != null && !quitting) window.Dispatcher.BeginInvoke(new Action(Tick)); };
            bridge = new BrowserBridge(HandleBrowser, isolatedPipeName);
            SystemEvents.SessionSwitch += SessionSwitch;
            SystemEvents.PowerModeChanged += PowerMode;
            window.Closing += delegate(object sender, System.ComponentModel.CancelEventArgs e) { if (!quitting) { e.Cancel = true; window.Hide(); } };
            window.IsVisibleChanged += delegate { UpdatePresentation(); if (window.IsVisible) { RenderSummary(); RenderSession(); } };
            window.StateChanged += delegate { UpdatePresentation(); };
            RefreshWindows(); RenderSummary(); RenderSect(); RenderSession();
            Message(save.Sect.Roster.Count == 0 ? "山门已候，在人物中免费邀请首位修炼者。" : "成果已留在宗门。准备好时，开启下一段专注。");
        }

        private void OpenFocus()
        {
            if (session == null && save.Sect.Roster.Count == 0) {
                sect.OpenPeople(); Message("先免费邀请一位修炼者，再开启专注。"); return;
            }
            sect.ClosePanel();
            SetFocusVisible(true);
        }
        private void SetFocusVisible(bool visible)
        {
            Get<Border>("FocusDrawer").Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
            sect.SetFocusOverlay(visible);
        }

        private void RefreshWindows()
        {
            var combo = Get<ComboBox>("WindowChoices"); var previous = combo.SelectedItem as WindowTarget;
            var windows = WindowsTracking.List(); combo.ItemsSource = windows;
            combo.SelectedItem = previous == null ? windows.FirstOrDefault(x => x.Name.Equals("Code", StringComparison.OrdinalIgnoreCase)) : windows.FirstOrDefault(x => x.Handle == previous.Handle);
            if (combo.SelectedItem == null && windows.Count > 0) combo.SelectedIndex = 0;
        }
        private void Start()
        {
            if (save.Sect.Roster.Count == 0) throw new InvalidOperationException("先在人物页免费选择一位修炼者入宗。");
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
            session = created; lastTick = clock.ElapsedMilliseconds; lastSave = lastTick; lastDistractions = 0;
            foregroundHook = WindowsTracking.SetWinEventHook(3, 3, IntPtr.Zero, foregroundCallback, 0, 0, 0);
            timer.Start(); RenderSession(); RenderSect(); window.Hide();
            tray.Text = "专注宗门 · 等待目标窗口";
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
                    tray.ShowBalloonTip(3500, "宗门静候归来", "当前已暂停累计，回来后继续。已有进度会保留。", Forms.ToolTipIcon.Info);
                }
                if (session.Complete) { Finish(); return; }
                if (now - lastSave >= 5000) { store.Save(save); lastSave = now; }
                tray.Text = "专注宗门 · " + TimeText(session.Data.EffectiveMs) + (session.Data.Paused ? " · 暂停" : session.Current == Presence.Allowed ? " · 专注中" : " · 等待返回");
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
            var priorAscended = save.Sect.Roster.Where(x => x.AscendedUtc != null).Select(x => x.Id).ToArray();
            SessionRecord result = null;
            try { SaveStore.Update(save, delegate(SaveData next) { result = Game.Settle(next, current.Data, false); }, store.Save); }
            catch { current.Pause(); throw; }
            current.Settled = true; session = null; timer.Stop();
            if (foregroundHook != IntPtr.Zero) { WindowsTracking.UnhookWinEvent(foregroundHook); foregroundHook = IntPtr.Zero; }
            tray.Text = "专注宗门 · 成果已保存";
            RenderSummary(); RenderSession(); RenderSect();
            if (result != null)
            {
                string text = "有效专注 " + (result.EffectiveMs / 60000.0).ToString("0.0") + " 分钟，获得 " + Rules.Materials(result.RewardUnits) + " 材料" + (result.ChallengeWon ? "（含挑战奖励）" : "") + "。";
                var ascended = save.Sect.Roster.Where(x => x.AscendedUtc != null && !priorAscended.Contains(x.Id)).Select(x => SectGame.Character(x.Id).Name).ToArray();
                if (ascended.Length > 0) text += " " + String.Join("、", ascended) + "已飞升，专属外观永久解锁。";
                Message(text + " 修炼与材料已保存，可稍后查看宗门。");
                tray.ShowBalloonTip(4000, "这一段投入，已留在宗门", text, Forms.ToolTipIcon.Info);
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
                    return new BrowserReply { Ok = true, Tracking = session != null && selectedBrowser && !session.Data.Paused, Selected = browser.Selected, Message = "已连接专注宗门" };
                }
                catch (ArgumentException ex) { return new BrowserReply { Message = ex.Message }; }
            }));
        }
        private void RenderSession()
        {
            bool active = session != null;
            Get<StackPanel>("SetupPanel").Visibility = active ? Visibility.Collapsed : Visibility.Visible;
            Get<StackPanel>("SessionPanel").Visibility = active ? Visibility.Visible : Visibility.Collapsed;
            Get<TextBox>("SectName").IsReadOnly = active;
            Get<TextBlock>("FocusHeading").Text = active ? "这一段专注" : "开启一段专注";
            Get<Button>("OpenFocus").Content = active ? "查看专注  →" : "准备专注  →";
            Get<TextBlock>("FocusHint").Text = active ? "宗门静候，专注当下" : "把此刻留给重要的事";
            Get<TextBlock>("FocusDockStatus").Text = active ? TimeText(session.Data.EffectiveMs) + " / " + session.Data.TargetMs / 60000 + " 分钟" : "有效专注，滋养宗门";
            UpdatePresentation();
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
            Get<TextBlock>("SectSummary").Text = "累计投入 " + (save.TotalEffectiveMs / 3600000.0).ToString("0.00") + " 小时 · 入宗 " + save.Sect.Roster.Count + " / 6 · 飞升 " + save.Sect.Roster.Count(x => x.AscendedUtc != null);
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
        private void RenderSect() { UpdatePresentation(); if (sect.PresentationEnabled) sect.Refresh(); }
        private void UpdatePresentation() { sect.SetPresentationEnabled(window.IsVisible && window.WindowState != WindowState.Minimized && session == null); }
        public void Message(string message) { Get<TextBlock>("StatusMessage").Text = message; }
        private void Commit(Action<SaveData> change)
        {
            if (session != null) throw new InvalidOperationException("请先结束专注，再调整宗门。");
            SaveStore.Update(save, change, store.Save);
        }
        private void Guard(Action action) { try { action(); } catch (Exception ex) { Message(ex.Message); } }
        public void ShowWindow() { window.Show(); window.WindowState = WindowState.Normal; window.Activate(); RenderSession(); if (session != null) OpenFocus(); }
        private void ShowBrowserHelp()
        {
            MessageBox.Show(window, "首次连接 Edge：\n\n1. 在 edge://extensions 打开开发人员模式，加载程序包中的 extension\\edge 文件夹。\n2. 在程序包目录打开 PowerShell，运行 scripts\\register-edge.ps1 -ExtensionId 该ID。\n3. 在普通网页点扩展图标，再点“选为专注页面”。\n\n设置仅需一次；移动程序包后重新注册即可。", "连接 Edge 页面", MessageBoxButton.OK, MessageBoxImage.Information);
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
            quitting = true; timer.Stop(); bridge.Dispose(); sect.Release();
            if (foregroundHook != IntPtr.Zero) WindowsTracking.UnhookWinEvent(foregroundHook);
            SystemEvents.SessionSwitch -= SessionSwitch; SystemEvents.PowerModeChanged -= PowerMode;
            tray.Visible = false; tray.Dispose(); trayIcon.Dispose();
        }
    }
}
