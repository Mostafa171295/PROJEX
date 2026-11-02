using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace Projex.ServerManager;

/// <summary>أدوات بناء الواجهة (ألوان وبطاقات وأزرار) لتوحيد الشكل في كل الصفحات.</summary>
internal static class Ui
{
    public const string Navy = "#0F2742";
    public const string NavyLight = "#1B3A5F";
    public const string Blue = "#2563A6";
    public const string Ink = "#172B4D";
    public const string Muted = "#64748B";
    public const string Line = "#E1E7EF";
    public const string Page = "#F4F6FA";
    public const string Green = "#15803D";
    public const string Red = "#B91C1C";
    public const string Amber = "#B45309";

    public static SolidColorBrush Brush(string color) => new(Color.Parse(color));

    public static string Arabic(string status) => status switch
    {
        "Running" => "يعمل",
        "Stopped" => "متوقف",
        "Not installed" => "غير مثبّت",
        "Online" => "متصل",
        "Offline" => "غير متصل",
        "Ready" => "جاهز",
        "Timed out" => "انتهت المهلة",
        "Not checked" => "لم يُفحص",
        "StartPending" => "جاري التشغيل…",
        "StopPending" => "جاري الإيقاف…",
        "Paused" => "متوقف مؤقتًا",
        "Checking…" => "جاري الفحص…",
        _ => status
    };

    public static string StatusColor(string status) => status switch
    {
        "Running" or "Online" or "Ready" => Green,
        "Not installed" or "Not checked" or "Checking…" or "StartPending" or "StopPending" => Amber,
        _ => Red
    };

    public static Control PageHeader(string eyebrow, string title, string description) => new StackPanel
    {
        Spacing = 5,
        Children =
        {
            new TextBlock { Text = eyebrow, FontSize = 12, FontWeight = FontWeight.Bold, Foreground = Brush(Blue) },
            new TextBlock { Text = title, FontSize = 28, FontWeight = FontWeight.Bold, Foreground = Brush(Ink) },
            new TextBlock { Text = description, FontSize = 14, Foreground = Brush(Muted), TextWrapping = TextWrapping.Wrap }
        }
    };

    public static Border Card(Control child, double padding = 18) => new()
    {
        Background = Brushes.White,
        BorderBrush = Brush(Line),
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(12),
        Padding = new Thickness(padding),
        Child = child
    };

    public static TextBlock SectionTitle(string text) => new()
    {
        Text = text,
        FontSize = 16,
        FontWeight = FontWeight.Bold,
        Foreground = Brush(Ink)
    };

    public static TextBlock Body(string text, string color = Muted, double size = 13) => new()
    {
        Text = text,
        FontSize = size,
        Foreground = Brush(color),
        TextWrapping = TextWrapping.Wrap
    };

    public static Button Primary(string label, Action? onClick = null)
    {
        var button = new Button
        {
            Content = label,
            MinHeight = 42,
            MinWidth = 150,
            Padding = new Thickness(18, 8),
            Background = Brush(Blue),
            Foreground = Brushes.White,
            FontWeight = FontWeight.SemiBold,
            CornerRadius = new CornerRadius(8)
        };
        if (onClick is not null) button.Click += (_, _) => onClick();
        return button;
    }

    public static Button Secondary(string label, Action? onClick = null)
    {
        var button = new Button
        {
            Content = label,
            MinHeight = 40,
            MinWidth = 120,
            Padding = new Thickness(14, 8),
            CornerRadius = new CornerRadius(8)
        };
        if (onClick is not null) button.Click += (_, _) => onClick();
        return button;
    }

    public static Button Secondary(string label, Func<Task> onClick)
    {
        var button = Secondary(label);
        button.Click += async (_, _) => await onClick();
        return button;
    }

    public static Button Primary(string label, Func<Task> onClick)
    {
        var button = Primary(label);
        button.Click += async (_, _) => await onClick();
        return button;
    }

    /// <summary>بطاقة حالة صغيرة: عنوان + قيمة ملوّنة + وصف.</summary>
    public static (Border Card, TextBlock Value) StatusCard(string title, string detail)
    {
        var value = new TextBlock { Text = "…", FontSize = 22, FontWeight = FontWeight.Bold, Foreground = Brush(Ink) };
        var card = Card(new StackPanel
        {
            Spacing = 6,
            Children =
            {
                new TextBlock { Text = title, FontSize = 12, FontWeight = FontWeight.Bold, Foreground = Brush(Muted) },
                value,
                new TextBlock { Text = detail, FontSize = 11, Foreground = Brush("#94A3B8"), TextWrapping = TextWrapping.Wrap }
            }
        });
        return (card, value);
    }

    public static void SetStatus(TextBlock target, string status)
    {
        target.Text = Arabic(status);
        target.Foreground = Brush(StatusColor(status));
    }

    public static Grid Row(params Control[] items)
    {
        var grid = new Grid { ColumnSpacing = 12 };
        for (var i = 0; i < items.Length; i++)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
            Grid.SetColumn(items[i], i);
            grid.Children.Add(items[i]);
        }
        return grid;
    }

    public static WrapPanel Buttons(params Control[] items)
    {
        var panel = new WrapPanel { Orientation = Orientation.Horizontal };
        foreach (var item in items)
        {
            item.Margin = new Thickness(0, 0, 10, 10);
            panel.Children.Add(item);
        }
        return panel;
    }

    public static void OpenPath(string path, Action<string> report)
    {
        try
        {
            if (Directory.Exists(path) || File.Exists(path) || path.EndsWith(".msc", StringComparison.OrdinalIgnoreCase))
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true });
            else
                report($"المسار غير موجود: {path}");
        }
        catch (Exception ex) { report($"تعذّر الفتح: {ex.Message}"); }
    }
}
