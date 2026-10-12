using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

internal static class ReadingTarget
{
    [STAThread]
    public static void Main()
    {
        var app = new Application();
        var panel = new StackPanel { Margin = new Thickness(38) };
        panel.Children.Add(new TextBlock { Text = "静静阅读，也是一段投入。", FontSize = 28, Margin = new Thickness(0, 0, 0, 24) });
        panel.Children.Add(new TextBlock { Text = "这是专注宗门的独立验证窗口。\n\n停留在这里，桌面客户端应累计有效时间。\n切换到其他窗口，应暂停累计；返回后继续。\n\n不需要移动鼠标或输入文字。", FontSize = 17, TextWrapping = TextWrapping.Wrap, LineHeight = 32 });
        app.Run(new Window { Title = "专注宗门验证 · 阅读目标", Width = 700, Height = 480, Content = panel, Background = new SolidColorBrush(Color.FromRgb(245, 244, 238)), FontFamily = new FontFamily("Microsoft YaHei UI"), WindowStartupLocation = WindowStartupLocation.CenterScreen });
    }
}
