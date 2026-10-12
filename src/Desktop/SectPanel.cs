using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using FocusOnYourWork.Core;

namespace FocusOnYourWork.Desktop
{
    public sealed class SectPanel : Grid
    {
        private readonly SaveData save;
        private readonly Action<Action<SaveData>> commit;
        private readonly Action<string> message;
        private readonly FrameworkElement themeRoot;
        private readonly ArtCatalog art;
        private Image scene;
        private Image portrait;
        private string selectedId = "sword";
        private string tab;
        private bool presentation;
        private bool distant;
        private bool focusOverlay;
        private FrameworkElement homeLocation;
        private FrameworkElement homeWelcome;
        public bool PresentationEnabled { get { return presentation; } }
        public int LoadedImageCount { get { return (scene != null && scene.Source != null ? 1 : 0) + (portrait != null && portrait.Source != null ? 1 : 0); } }

        public SectPanel(SaveData save, FrameworkElement themeRoot, Action<Action<SaveData>> commit, Action<string> message)
        {
            this.save = save; this.themeRoot = themeRoot; this.commit = commit; this.message = message;
            art = new ArtCatalog(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Art"));
            art.ApplyTheme(themeRoot, save.Sect.UiTheme);
            if (save.Sect.Roster.Count > 0) selectedId = save.Sect.Roster[0].Id;
        }
        public void SetPresentationEnabled(bool enabled)
        {
            if (presentation == enabled) return;
            bool hadImages = LoadedImageCount > 0;
            presentation = enabled; Refresh();
            if (!enabled && hadImages) Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(delegate {
                if (presentation) return;
                // WPF bitmap backing stores are unmanaged; quiet focus may otherwise delay their finalizers.
                // Reclaim unreachable images once per transition, never trim the process working set.
                System.Threading.ThreadPool.QueueUserWorkItem(delegate {
                    GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
                });
            }));
        }
        public void Release()
        {
            if (scene != null) scene.Source = null;
            if (portrait != null) portrait.Source = null;
            scene = null; portrait = null; homeLocation = null; homeWelcome = null; Children.Clear(); RowDefinitions.Clear();
        }
        private static TextBlock Text(string text, double size)
        { return new TextBlock { Text = text, FontSize = size, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 9) }; }
        private Button Button(string label, Action action, bool enabled)
        {
            var b = new Button { Content = label, IsEnabled = enabled };
            b.Click += delegate { try { action(); } catch (Exception ex) { message(ex.Message); } };
            return b;
        }
        private void Change(Action<SaveData> action, string notice)
        { commit(action); Refresh(); message(notice); }
        public event Action NavigationChanged;
        public string OpenPanel { get { return tab; } }
        public void SetFocusOverlay(bool visible)
        {
            focusOverlay = visible;
            if (homeLocation != null) homeLocation.Visibility = visible ? Visibility.Collapsed : Visibility.Visible;
            if (homeWelcome != null) homeWelcome.Visibility = visible ? Visibility.Collapsed : Visibility.Visible;
        }
        public void ClosePanel()
        {
            if (tab == null) return;
            tab = null; Refresh();
        }
        public void OpenPeople() { Navigate("人物"); }
        private void Navigate(string target)
        {
            tab = target;
            if (NavigationChanged != null) NavigationChanged();
            Refresh();
        }
        private static TextBlock HudText(string text, double size, string color)
        {
            var label = Text(text, size); label.Foreground = ArtCatalog.Brush(color); return label;
        }
        private static LinearGradientBrush Veil(string from, string to, Point end)
        {
            var brush = new LinearGradientBrush((Color)ColorConverter.ConvertFromString(from), (Color)ColorConverter.ConvertFromString(to), new Point(0, 0), end);
            brush.Freeze(); return brush;
        }
        public void Refresh()
        {
            Release();
            if (!presentation) {
                Background = Veil("#183D36", "#0D201F", new Point(1, 1));
                var quiet = new StackPanel { HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(450, 0, 70, 0), MaxWidth = 430 };
                quiet.Children.Add(HudText("山门静候", 36, "#FFF9EC"));
                quiet.Children.Add(HudText("专注于眼前，修行自会发生。", 17, "#D7E3DA"));
                Children.Add(quiet); return;
            }
            Background = ArtCatalog.Brush("#193B35");
            art.ApplyTheme(themeRoot, save.Sect.UiTheme);
            var stage = new Grid { Name = "WorldStage", ClipToBounds = true };
            scene = new Image { Source = art.Scene(save.Sect.SceneTheme), Stretch = Stretch.UniformToFill };
            if (distant && save.Sect.MountainLevel == 3) { scene.RenderTransformOrigin = new Point(.7,.3); scene.RenderTransform = new ScaleTransform(1.3, 1.3); }
            stage.Children.Add(scene);
            stage.Children.Add(new Border { Background = Veil("#DA102D29", "#00102D29", new Point(0, 1)), Height = 210, VerticalAlignment = VerticalAlignment.Top, IsHitTestVisible = false });
            stage.Children.Add(new Border { Background = Veil("#00102D29", "#ED102D29", new Point(0, 1)), Height = 330, VerticalAlignment = VerticalAlignment.Bottom, IsHitTestVisible = false });
            stage.Children.Add(new Border { Background = Veil("#60102D29", "#00102D29", new Point(1, 0)), Width = 620, HorizontalAlignment = HorizontalAlignment.Left, IsHitTestVisible = false });
            portrait = new Image { Name = "WorldPortrait", Source = art.Portrait(selectedId), Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, Width = 400, MaxHeight = 610, Margin = new Thickness(0, 180, 42, 120), IsHitTestVisible = false };
            stage.Children.Add(portrait);
            Children.Add(stage);
            var person = save.Sect.Roster.FirstOrDefault(x => x.Id == selectedId);
            var definition = SectGame.Character(selectedId);
            var characterLabel = new StackPanel { HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 112, 40, 0), MaxWidth = 300 };
            characterLabel.Children.Add(HudText(definition.Path + "  /  " + (person == null ? "待结缘" : person.AscendedUtc != null ? "已飞升" : person.Active ? "修炼中" : "候修"), 12, "#F0E4C4"));
            var name = HudText(definition.Name, 29, "#FFF9EC"); name.FontWeight = FontWeights.SemiBold; characterLabel.Children.Add(name);
            if (person != null) characterLabel.Children.Add(HudText(SectGame.Realm(person.CultivationMs) + " · " + (person.CultivationMs / 3600000.0).ToString("0.00") + " / 99 小时", 12, "#FFF9EC"));
            Children.Add(characterLabel);
            if (portrait.Source == null || scene.Source == null || !art.HasScene(save.Sect.SceneTheme) || art.Warning != null) {
                var missing = HudText(art.Warning ?? "当前场景或立绘待补齐，可在外观中重载美术。", 12, "#FFF9EC"); missing.Margin = new Thickness(12);
                Children.Add(new Border { Background = ArtCatalog.Brush("#E0263D38"), Child = missing, MaxWidth = 460, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 96, 0, 0) });
            }
            if (tab == null) {
                var location = new StackPanel { HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(32, 132, 0, 0), MaxWidth = 370, IsHitTestVisible = false };
                location.Children.Add(HudText("宗 门 · " + (distant && save.Sect.MountainLevel == 3 ? "观云台" : "山门"), 12, "#E9DAB5"));
                var title = HudText(save.Sect.SceneTheme == "default" ? "青绿山门" : SectGame.Character(save.Sect.SceneTheme).ThemeName, 38, "#FFF9EC"); title.FontFamily = new FontFamily("SimSun"); location.Children.Add(title);
                location.Children.Add(HudText("灵山 " + save.Sect.MountainLevel + " 阶  ·  修炼位 " + save.Sect.Roster.Count(x => x.Active) + " / " + save.Sect.Capacity, 13, "#FFF9EC"));
                homeLocation = location; Children.Add(location);
                var welcome = new StackPanel { Margin = new Thickness(18, 15, 18, 10) };
                welcome.Children.Add(HudText(save.Sect.Roster.Count == 0 ? "初入山门 · 择一人同行" : "修行在日常，山水待君归", 18, "#FFF9EC"));
                welcome.Children.Add(HudText(save.Sect.Roster.Count == 0 ? "在「人物」中免费邀请首位修炼者，\n让每一段有效专注，留在宗门。" : "每位修炼者独立积累 99 小时。\n休息不退步，准备好时再启程。", 13, "#D7E3DA"));
                if (save.Sect.Memorials.Count > 0) welcome.Children.Add(HudText("祖师留影 · " + String.Join(" / ", save.Sect.Memorials.Select(id => SectGame.Character(id).Name)), 12, "#E9DAB5"));
                homeWelcome = new Border { Background = ArtCatalog.Brush("#CC17362F"), BorderBrush = ArtCatalog.Brush("#667F9C8E"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10), Child = welcome, Width = 355, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(28, 0, 0, 148) };
                Children.Add(homeWelcome); SetFocusOverlay(focusOverlay);
            }
            var tabs = new StackPanel { Orientation = Orientation.Horizontal };
            foreach (string label in new [] { "山门", "人物", "建设", "外观" }) {
                string target = label == "山门" ? null : label;
                var button = Button(label, delegate { Navigate(target); }, true);
                button.Style = (Style)FindResource("SceneNavButton");
                if (tab == target) { button.Background = ArtCatalog.Brush("#D9E4D6C1"); button.Foreground = ArtCatalog.Brush("#263D38"); }
                tabs.Children.Add(button);
            }
            Children.Add(new Border { Child = tabs, Background = ArtCatalog.Brush("#DC172E2B"), BorderBrush = ArtCatalog.Brush("#527D9686"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12), Padding = new Thickness(8), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(28, 0, 0, 52) });
            if (tab == null) return;
            var drawerGrid = new Grid();
            drawerGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            drawerGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            var header = new DockPanel { Margin = new Thickness(0, 0, 0, 18) };
            var close = Button("收起", delegate { Navigate(null); }, true); close.Style = (Style)FindResource("PanelCloseButton"); close.VerticalAlignment = VerticalAlignment.Top;
            DockPanel.SetDock(close, Dock.Right); header.Children.Add(close);
            var heading = new StackPanel();
            var eyebrow = Text(tab == "人物" ? "同 道 同 行" : tab == "建设" ? "经 营 山 门" : "山 水 新 装", 11); eyebrow.SetResourceReference(TextBlock.ForegroundProperty, "Muted"); heading.Children.Add(eyebrow);
            var headingText = Text(tab == "人物" ? "宗门人物" : tab == "建设" ? "洞府与灵山" : "宗门外观", 22); headingText.FontWeight = FontWeights.SemiBold; headingText.Margin = new Thickness(0); heading.Children.Add(headingText);
            header.Children.Add(heading); drawerGrid.Children.Add(header);
            var content = new StackPanel { Margin = new Thickness(0, 0, 4, 0) };
            var scroll = new ScrollViewer { Content = content, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
            Grid.SetRow(scroll, 1); drawerGrid.Children.Add(scroll);
            var drawer = new Border { Name = "ManagementDrawer", Child = drawerGrid, Style = (Style)FindResource("ScenePanel"), Width = 390, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(28, 112, 0, 126) };
            Children.Add(drawer);
            if (tab == "人物") People(content);
            else if (tab == "建设") Construction(content);
            else Appearance(content);
        }
        private void People(StackPanel panel)
        {
            var selector = new ComboBox { Margin = new Thickness(0,0,0,10) };
            foreach (var d in SectGame.Characters) {
                var c = save.Sect.Roster.FirstOrDefault(x => x.Id == d.Id);
                selector.Items.Add(new ComboBoxItem { Content = d.Name + " · " + d.Path + " · " + (c == null ? "未结缘" : c.AscendedUtc != null ? "已飞升" : c.Active ? "修炼中" : "候修"), Tag = d.Id });
            }
            selector.SelectedIndex = Array.FindIndex(SectGame.Characters, x => x.Id == selectedId);
            selector.SelectionChanged += delegate { if (selector.SelectedItem != null) { selectedId = (string)((ComboBoxItem)selector.SelectedItem).Tag; Refresh(); } };
            panel.Children.Add(selector);
            var definition = SectGame.Character(selectedId);
            var person = save.Sect.Roster.FirstOrDefault(x => x.Id == selectedId);
            panel.Children.Add(Text(definition.Story,14));
            if (person != null) {
                string state = person.AscendedUtc != null ? "已飞升 · " + definition.ThemeName + " 已永久解锁" : SectGame.Realm(person.CultivationMs) + " · " + (person.CultivationMs / 3600000.0).ToString("0.00") + " / 99 小时";
                panel.Children.Add(Text(state,18));
                panel.Children.Add(new ProgressBar { Value = person.CultivationMs * 100.0 / SectGame.AscensionMs, Height = 5, Margin = new Thickness(0,0,0,10) });
                if (person.AscendedUtc != null) {
                    panel.Children.Add(Text("飞升记录 " + DateTime.Parse(person.AscendedUtc).ToLocalTime().ToString("yyyy-MM-dd") + " · 最后一段投入：" + person.LastTask,12));
                    panel.Children.Add(Button(save.Sect.Memorials.Contains(person.Id) ? "收起祖师纪念" : "展示祖师纪念", delegate { Change(s => SectGame.ToggleMemorial(s, selectedId), "纪念展示已保存。"); }, true));
                } else if (!person.Active) panel.Children.Add(Button("优先候修", delegate { Change(s => SectGame.PrioritizeWaiting(s, selectedId), "已调整候修顺序，当前修炼者不受影响。"); }, true));
                else panel.Children.Add(Text("下一境界 " + SectGame.Realm(SectGame.NextRealmMs(person.CultivationMs)) + " · 自动修炼，休息不退步。",12));
            } else panel.Children.Add(Text("飞升外观 · " + definition.ThemeName + "。入驻后独立修炼 99 小时。",14));
            if (save.Sect.Roster.Count == 0) {
                panel.Children.Add(Button("邀此人入宗 · 免费", delegate { Change(s => SectGame.ChooseStarter(s, selectedId), "初始人物已入宗。下一段有效专注会开始修炼。"); }, true)); return;
            }
            if (save.Sect.PendingDrawId != null) {
                panel.Children.Add(Text("保底已付费，请选择一名未收录人物；关闭后仍保留。",14));
                panel.Children.Add(Button("确认保底选择", delegate { string id = save.Sect.PendingDrawId; Change(s => SectGame.ChoosePity(s, id, selectedId), "保底人物已入宗。"); }, person == null));
            } else {
                panel.Children.Add(Button(save.Sect.Roster.Count == 6 ? "本期人物已全部收录" : "结缘一次 · 120 材料", Draw, save.Sect.Roster.Count < 6 && save.WalletUnits >= SectGame.DrawCost));
                panel.Children.Add(Text("每人等概率 1/6 · 最多 " + (5-save.Sect.DuplicateStreak) + " 抽保证新人物 · 重复返 12 材料",12));
                var receipt = save.Sect.Draws.LastOrDefault();
                if (receipt != null && receipt.CharacterId != null) panel.Children.Add(Text("最近结缘：" + SectGame.Character(receipt.CharacterId).Name + (receipt.Duplicate ? " · 重复，已返 12 材料" : " · 已收录"),12));
            }
        }
        private void Draw()
        {
            int roll;
            using (var rng = RandomNumberGenerator.Create()) { byte[] b = new byte[1]; do { rng.GetBytes(b); } while (b[0] >= 252); roll = b[0] % 6; }
            DrawReceipt receipt = null; string request = Guid.NewGuid().ToString("N");
            commit(s => { receipt = SectGame.Draw(s,request,roll); });
            if (receipt.CharacterId != null) selectedId = receipt.CharacterId;
            else selectedId = SectGame.Characters.First(x => !save.Sect.Roster.Any(c => c.Id == x.Id)).Id;
            Refresh(); message(receipt.Choice ? "已触发保底，选择一位未收录人物即可。" : receipt.Duplicate ? "重复结缘，已返还 12 材料。" : SectGame.Character(receipt.CharacterId).Name + "已入宗，空位自动修炼，满员则候修。");
        }
        private void Construction(StackPanel panel)
        {
            panel.Children.Add(Text("洞府 · " + save.Sect.Capacity + " 个修炼位",20));
            panel.Children.Add(Text("在修者各自随有效专注成长；候修者在空位出现时自动入住。",13));
            long housing = SectGame.HousingPrice(save.Sect);
            panel.Children.Add(Button(housing == 0 ? "洞府已完成" : "扩建洞府 · " + Rules.Materials(housing) + " 材料", delegate { Change(SectGame.UpgradeHousing,"洞府已扩建，候修人物已自动补位。"); }, housing > 0 && save.WalletUnits >= housing));
            panel.Children.Add(Text("灵山 " + save.Sect.MountainLevel + " 阶 · 纪念位置 " + (save.Sect.MountainLevel-1),18));
            long mountain = SectGame.MountainPrice(save.Sect);
            panel.Children.Add(Button(mountain == 0 ? "灵山已完成" : "修缮灵山 · " + Rules.Materials(mountain) + " 材料", delegate { Change(SectGame.UpgradeMountain,"灵山已修缮，新的纪念位置已开放。"); }, mountain > 0 && save.WalletUnits >= mountain));
            if (save.Sect.MountainLevel == 3) panel.Children.Add(Button(distant ? "回到山门" : "登观云台", delegate { distant = !distant; Refresh(); }, true));
        }
        private void Appearance(StackPanel panel)
        {
            panel.Children.Add(Text("场景与界面可以独立搭配",16));
            panel.Children.Add(Text("人物飞升后解锁对应主题，无额外属性或使用费用。",13));
            var themes = new [] { "default" }.Concat(SectGame.Characters.Select(x => x.Id));
            foreach (string id in themes) {
                string chosen = id; bool unlocked = SectGame.ThemeUnlocked(save.Sect, chosen);
                var group = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
                group.Children.Add(Text((id == "default" ? "青绿山门" : SectGame.Character(id).ThemeName) + (unlocked ? "" : " · 飞升后解锁"), 14));
                var row = new WrapPanel();
                row.Children.Add(Button(!art.HasScene(id) ? "场景待补齐" : save.Sect.SceneTheme == id ? "场景已应用" : "场景", delegate { Change(s => SectGame.SetTheme(s,chosen,false),"场景主题已保存。"); }, unlocked && art.HasScene(id)));
                row.Children.Add(Button(save.Sect.UiTheme == id ? "界面已应用" : "界面", delegate { Change(s => SectGame.SetTheme(s,chosen,true),"界面主题已保存。"); }, unlocked));
                group.Children.Add(row); panel.Children.Add(group);
            }
            panel.Children.Add(Button("重载美术", delegate { art.Reload(); Refresh(); message("资源清单与图片已重新加载。"); }, true));
        }
    }
}
