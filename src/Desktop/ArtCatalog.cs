using System;
using System.IO;
using System.Linq;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace FocusOnYourWork.Desktop
{
    public sealed class ArtTheme
    {
        public string Id { get; set; }
        public string Scene { get; set; }
        public string Accent { get; set; }
        public string Paper { get; set; }
        public string Ink { get; set; }
        public string Muted { get; set; }
        public string Emblem { get; set; }
    }
    public sealed class ArtPortrait { public string Id { get; set; } public string File { get; set; } }
    public sealed class ArtManifest { public int Version { get; set; } public ArtTheme[] Themes { get; set; } public ArtPortrait[] Portraits { get; set; } }
    // Plain filenames and stable IDs, no theme inheritance and no gameplay dependency on art files.
    public sealed class ArtCatalog
    {
        private readonly string directory;
        private ArtManifest manifest;
        public string Warning { get; private set; }
        public ArtCatalog(string directory)
        {
            this.directory = directory;
            manifest = new ArtManifest { Version = 1, Portraits = new ArtPortrait[0], Themes = new [] {
                new ArtTheme { Id = "default", Scene = "scene-default.png", Accent = "#315E58", Paper = "#F6F2E9", Ink = "#263D38", Muted = "#687C70" }
            } };
            try { Reload(); } catch (Exception ex) { Warning = "美术清单读取失败，已使用默认样式：" + ex.Message; }
        }
        public void Reload()
        {
            var next = new JavaScriptSerializer().Deserialize<ArtManifest>(File.ReadAllText(Path.Combine(directory, "manifest.json")));
            if (next == null || next.Version != 1 || next.Themes == null || next.Portraits == null || next.Themes.Any(x => x == null || String.IsNullOrWhiteSpace(x.Id)) ||
                next.Portraits.Any(x => x == null || String.IsNullOrWhiteSpace(x.Id)) || !next.Themes.Any(x => x.Id == "default") ||
                next.Themes.Select(x => x.Id).Distinct().Count() != next.Themes.Length || next.Portraits.Select(x => x.Id).Distinct().Count() != next.Portraits.Length)
                throw new InvalidDataException("美术资源清单无效。");
            foreach (var theme in next.Themes) { AssetPath(theme.Scene); Brush(theme.Accent); Brush(theme.Paper); Brush(theme.Ink); Brush(theme.Muted); if (theme.Emblem != null) Geometry.Parse(theme.Emblem); }
            foreach (var portrait in next.Portraits) AssetPath(portrait.File);
            manifest = next; Warning = null;
        }
        private string AssetPath(string file)
        {
            if (String.IsNullOrWhiteSpace(file) || file != Path.GetFileName(file) || file.Contains("..") || file.IndexOfAny(new [] { '/', '\\', ':' }) >= 0)
                throw new InvalidDataException("美术路径只能是 Art 文件夹中的文件名。");
            return Path.Combine(directory, file);
        }
        public static SolidColorBrush Brush(string hex)
        { var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)); brush.Freeze(); return brush; }
        public void ApplyTheme(FrameworkElement root, string id)
        {
            var t = manifest.Themes.FirstOrDefault(x => x.Id == id) ?? manifest.Themes.First(x => x.Id == "default");
            root.Resources["Accent"] = Brush(t.Accent); root.Resources["Paper"] = Brush(t.Paper);
            root.Resources["Ink"] = Brush(t.Ink); root.Resources["Muted"] = Brush(t.Muted);
            var emblem = Geometry.Parse(t.Emblem ?? "M 0,24 L 12,0 L 24,24 M 5,14 L 12,8 L 19,14"); emblem.Freeze(); root.Resources["Emblem"] = emblem;
            var edgeColor = (Color)ColorConverter.ConvertFromString(t.Accent); edgeColor.A = 55;
            var edge = new SolidColorBrush(edgeColor); edge.Freeze(); root.Resources["ThemeEdge"] = edge;
            var panelColor = (Color)ColorConverter.ConvertFromString(t.Paper); panelColor.A = 245;
            var panel = new SolidColorBrush(panelColor); panel.Freeze(); root.Resources["OverlayPaper"] = panel;
        }
        private BitmapSource Load(string file, int width)
        {
            string path = AssetPath(file); if (!File.Exists(path)) return null;
            // A fresh stream bypasses URI caching; OnLoad releases it for same-name replacement.
            try { using (var stream = File.OpenRead(path)) {
                var image = new BitmapImage(); image.BeginInit(); image.CacheOption = BitmapCacheOption.OnLoad;
                image.DecodePixelWidth = width;
                image.StreamSource = stream; image.EndInit(); image.Freeze(); return image;
            } } catch (Exception ex) {
                if (!(ex is IOException || ex is NotSupportedException || ex is ArgumentException || ex is InvalidOperationException)) throw;
                Warning = "素材暂不可用：" + file + " · " + ex.Message; return null;
            }
        }
        public bool HasScene(string id)
        { var t = manifest.Themes.FirstOrDefault(x => x.Id == id); return t != null && File.Exists(AssetPath(t.Scene)); }
        public BitmapSource Scene(string id)
        {
            var fallback = manifest.Themes.First(x => x.Id == "default");
            var t = manifest.Themes.FirstOrDefault(x => x.Id == id) ?? fallback;
            return Load(t.Scene, 1600) ?? (t == fallback ? null : Load(fallback.Scene, 1600));
        }
        public BitmapSource Portrait(string id)
        { var p = manifest.Portraits.FirstOrDefault(x => x.Id == id); return p == null ? null : Load(p.File, 520); }
    }
}
