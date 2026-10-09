using System;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using FocusIsland.Core;

namespace FocusIsland.Desktop
{
    public sealed class IslandView : FrameworkElement
    {
        public SaveData Save { get; set; }
        public int SelectedPlot { get; set; }
        public string PreviewKind { get; set; }
        public string PreviewColor { get; set; }
        public bool CanEdit { get; set; }
        public event Action<int> PlotClicked;
        private static SolidColorBrush Brush(string color)
        {
            var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color)); b.Freeze(); return b;
        }
        private static void Shape(DrawingContext d, string color, string path)
        {
            var g = Geometry.Parse(path); g.Freeze(); d.DrawGeometry(Brush(color), null, g);
        }
        private static Point Plot(int i) { int row = i / 3, col = i % 3; return new Point(290 + (col - row) * 82, 118 + (col + row) * 35); }
        private static void Label(DrawingContext d, string text, double x, double y, string color, double size)
        {
            d.DrawText(new FormattedText(text, CultureInfo.GetCultureInfo("zh-CN"), FlowDirection.LeftToRight,
                new Typeface("Microsoft YaHei UI"), size, Brush(color), 1.0), new Point(x, y));
        }
        protected override void OnRender(DrawingContext d)
        {
            base.OnRender(d);
            if (ActualWidth <= 0 || ActualHeight <= 0 || Save == null) return;
            double scale = Math.Min(ActualWidth / 580.0, ActualHeight / 355.0);
            d.PushTransform(new TranslateTransform((ActualWidth - 580 * scale) / 2, (ActualHeight - 355 * scale) / 2));
            d.PushTransform(new ScaleTransform(scale, scale));
            d.DrawEllipse(Brush("#C1D3C7"), null, new Point(287, 290), 205, 34);
            Shape(d, "#C7B69A", "M 57,205 C 60,137 195,83 295,82 C 431,83 527,150 526,213 L 512,245 C 467,306 189,345 87,264 Z");
            Shape(d, "#DDE2BB", "M 57,198 C 71,129 185,78 299,81 C 423,80 526,142 526,203 C 505,263 364,295 242,291 C 144,288 59,252 57,198 Z");
            Shape(d, "#A5BD8A", "M 75,188 C 89,124 207,86 298,92 C 417,89 504,150 505,198 C 486,247 365,276 247,277 C 152,270 79,240 75,188 Z");
            Shape(d, "#BFD09A", "M 86,187 C 118,127 241,104 319,113 C 390,117 465,151 482,190 C 448,207 369,200 308,233 C 238,272 131,241 86,218 Z");
            // Small permanent landscape details; the nine plots remain available for personal choices.
            d.DrawEllipse(Brush("#D8DCB8"), null, new Point(445, 229), 13, 6);
            d.DrawEllipse(Brush("#8FA883"), null, new Point(451, 224), 9, 6);
            d.DrawEllipse(Brush("#879F7C"), null, new Point(125, 177), 7, 4);
            d.DrawEllipse(Brush("#6F916F"), null, new Point(134, 170), 4, 10);
            for (int i = 0; i < Rules.PlotCount; i++)
            {
                Point p = Plot(i); Building building = Save.Buildings.FirstOrDefault(x => x.Plot == i);
                var tile = new StreamGeometry(); using (var c = tile.Open()) {
                    c.BeginFigure(new Point(p.X, p.Y - 17), true, true);
                    c.LineTo(new Point(p.X + 37, p.Y), true, false); c.LineTo(new Point(p.X, p.Y + 17), true, false); c.LineTo(new Point(p.X - 37, p.Y), true, false);
                }
                d.DrawGeometry(Brush(i == SelectedPlot ? "#E3E8C3" : "#ACBF8C"), new Pen(Brush(i == SelectedPlot ? "#527857" : "#9FB783"), i == SelectedPlot ? 2 : 1), tile);
                if (building != null) DrawBuilding(d, p, building.Kind, building.Color);
                else if (i == SelectedPlot && CanEdit)
                {
                    d.PushOpacity(0.40); DrawBuilding(d, p, PreviewKind ?? "tree", PreviewColor ?? "sage"); d.Pop();
                    Label(d, "预览", p.X - 13, p.Y + 17, "#496342", 11);
                }
                else Label(d, (i + 1).ToString(), p.X - 4, p.Y - 7, "#657E58", 12);
            }
            Label(d, "每一处变化，都来自你的投入", 194, 325, "#63785F", 12);
            d.Pop(); d.Pop();
        }
        private static void DrawBuilding(DrawingContext d, Point p, string kind, string color)
        {
            string roof = color == "clay" ? "#B77C5D" : "#527F6B";
            d.PushTransform(new TranslateTransform(p.X, p.Y));
            d.DrawEllipse(Brush("#8EA576"), null, new Point(2, 0), 26, 10);
            if (kind == "tree")
            {
                d.DrawRoundedRectangle(Brush("#9D7751"), null, new Rect(-4, -31, 8, 33), 2, 2);
                d.DrawEllipse(Brush(color == "clay" ? "#BA8666" : "#638B65"), null, new Point(-7, -39), 23, 28);
                d.DrawEllipse(Brush(color == "clay" ? "#D3A079" : "#85A977"), null, new Point(10, -49), 20, 27);
                d.DrawEllipse(Brush(color == "clay" ? "#DEC295" : "#ACC391"), null, new Point(8, -64), 13, 12);
            }
            else if (kind == "tent")
            {
                Shape(d, "#DED8B6", "M -33,0 L -7,-49 L 27,-2 L 0,12 Z");
                Shape(d, roof, "M -7,-49 L 25,-39 L 47,-4 L 27,-2 Z");
                Shape(d, "#685C44", "M -18,3 L -7,-29 L 11,7 Z");
                d.DrawLine(new Pen(Brush("#7F8060"), 2), new Point(-7, -50), new Point(-35, 5));
                d.DrawEllipse(Brush("#C89B62"), null, new Point(32, 10), 6, 3);
            }
            else
            {
                Shape(d, "#F0E6CA", "M -28,-36 L 13,-43 L 13,2 L -28,8 Z");
                Shape(d, "#DCCDAA", "M 13,-43 L 36,-27 L 36,-1 L 13,2 Z");
                Shape(d, roof, "M -35,-34 L -11,-65 L 40,-31 L 13,-36 Z");
                Shape(d, color == "clay" ? "#986247" : "#3F6959", "M -11,-65 L 16,-68 L 43,-31 L 13,-36 Z");
                d.DrawRectangle(Brush("#927C58"), null, new Rect(-13, -15, 11, 21));
                d.DrawRoundedRectangle(Brush("#B8D1C3"), new Pen(Brush("#F7F0D9"), 2), new Rect(-20, -31, 10, 10), 1, 1);
                d.DrawRectangle(Brush("#C8A888"), null, new Rect(13, -69, 9, 15));
            }
            d.Pop();
        }
        protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonDown(e); if (!CanEdit) return;
            double scale = Math.Min(ActualWidth / 580.0, ActualHeight / 355.0);
            Point raw = e.GetPosition(this);
            Point point = new Point((raw.X - (ActualWidth - 580 * scale) / 2) / scale, (raw.Y - (ActualHeight - 355 * scale) / 2) / scale);
            int best = -1; double distance = Double.MaxValue;
            for (int i = 0; i < Rules.PlotCount; i++) {
                Point p = Plot(i); double dist = Math.Pow((point.X - p.X) / 2, 2) + Math.Pow(point.Y - p.Y, 2);
                if (dist < distance) { best = i; distance = dist; }
            }
            if (distance < 650 && PlotClicked != null) PlotClicked(best);
        }
    }
}
