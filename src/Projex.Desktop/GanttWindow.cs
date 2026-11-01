using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Avalonia.Layout;

namespace Projex.Desktop;

/// <summary>
/// First desktop Gantt presentation slice. The fixture is intentionally labeled as sample data;
/// the API-backed project adapter and editing commands are introduced in subsequent slices.
/// </summary>
public sealed class GanttWindow : Window
{
    private readonly Canvas _timeline = new() { Background = Brushes.White };
    private readonly StackPanel _taskRows = new() { Spacing = 0 };
    private readonly TextBlock _selection = new() { Text = "Select an activity to inspect its schedule.", TextWrapping = TextWrapping.Wrap };
    private readonly List<GanttRow> _activities = CreateSample();
    private double _dayWidth = 30;
    private const double RowHeight = 48;
    private const double HeaderHeight = 48;

    public GanttWindow()
    {
        Title = "PROJEX ENTERPRISE — Planning & Scheduling";
        Width = 1440;
        Height = 900;
        MinWidth = 1080;
        MinHeight = 680;
        Content = BuildLayout();
        RenderSchedule();
    }

    private Control BuildLayout()
    {
        var root = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,*"),
            Background = Brush("#F5F7FA")
        };
        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto,Auto"), Margin = new Thickness(24, 18) };
        var titleStack = new StackPanel { Spacing = 4 };
        titleStack.Children.Add(new TextBlock { Text = "Planning & Scheduling", FontSize = 25, FontWeight = FontWeight.Bold, Foreground = Brush("#172B4D") });
        titleStack.Children.Add(new TextBlock { Text = "Gantt timeline · Baseline comparison · Critical path", Foreground = Brush("#64748B") });
        header.Children.Add(titleStack);
        var sample = new Border { Background = Brush("#FFF3D6"), CornerRadius = new CornerRadius(12), Padding = new Thickness(12, 7), Margin = new Thickness(10, 5) };
        sample.Child = new TextBlock { Text = "SAMPLE DATA — PREVIEW", Foreground = Brush("#8A5A00"), FontSize = 11, FontWeight = FontWeight.Bold };
        Grid.SetColumn(sample, 1); header.Children.Add(sample);
        var zoomOut = new Button { Content = "−", Width = 38, Margin = new Thickness(4) };
        zoomOut.Click += (_, _) => { _dayWidth = Math.Max(16, _dayWidth - 4); RenderSchedule(); };
        Grid.SetColumn(zoomOut, 2); header.Children.Add(zoomOut);
        var zoomIn = new Button { Content = "+", Width = 38, Margin = new Thickness(4) };
        zoomIn.Click += (_, _) => { _dayWidth = Math.Min(56, _dayWidth + 4); RenderSchedule(); };
        Grid.SetColumn(zoomIn, 3); header.Children.Add(zoomIn);
        root.Children.Add(header);

        var body = new Grid { ColumnDefinitions = new ColumnDefinitions("340,*"), RowDefinitions = new RowDefinitions("*,Auto"), Margin = new Thickness(18, 0, 18, 18) };
        var left = new Border { Background = Brushes.White, BorderBrush = Brush("#DCE3EC"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8, 0, 0, 8) };
        var leftStack = new StackPanel { Spacing = 0 };
        leftStack.Children.Add(new Border { Height = HeaderHeight, Background = Brush("#EEF2F7"), Padding = new Thickness(16, 14), Child = new TextBlock { Text = "ACTIVITY / WBS", FontWeight = FontWeight.Bold, Foreground = Brush("#475569") } });
        _taskRows.Children.Clear();
        leftStack.Children.Add(_taskRows);
        left.Child = new ScrollViewer { Content = leftStack, VerticalScrollBarVisibility = ScrollBarVisibility.Hidden };
        body.Children.Add(left);

        var right = new Border { Background = Brushes.White, BorderBrush = Brush("#DCE3EC"), BorderThickness = new Thickness(0, 1, 1, 1), CornerRadius = new CornerRadius(0, 8, 8, 0), ClipToBounds = true };
        right.Child = new ScrollViewer { Content = _timeline, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        Grid.SetColumn(right, 1); body.Children.Add(right);

        var details = new Border { Background = Brushes.White, BorderBrush = Brush("#DCE3EC"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Padding = new Thickness(14), Margin = new Thickness(0, 12, 0, 0) };
        details.Child = _selection;
        Grid.SetRow(details, 1); Grid.SetColumnSpan(details, 2); body.Children.Add(details);
        Grid.SetRow(body, 1); root.Children.Add(body);
        return root;
    }

    private void RenderSchedule()
    {
        _timeline.Children.Clear();
        _taskRows.Children.Clear();
        var start = new DateOnly(2026, 11, 2);
        var totalDays = 35;
        var width = totalDays * _dayWidth + 24;
        var height = HeaderHeight + _activities.Count * RowHeight;
        _timeline.Width = width;
        _timeline.Height = height;

        AddRect(_timeline, 0, 0, width, HeaderHeight, "#EEF2F7");
        for (var day = 0; day < totalDays; day++)
        {
            var x = 12 + day * _dayWidth;
            AddLine(_timeline, x, HeaderHeight, x, height, day % 7 == 0 ? "#CBD5E1" : "#E8EDF3", day % 7 == 0 ? 1.4 : 0.7);
            if (day % 2 == 0)
                AddText(_timeline, start.AddDays(day).ToString("dd MMM"), x + 2, 14, 10, "#64748B");
        }

        // Dependency connectors are drawn first so activity bars remain visually prominent.
        foreach (var row in _activities)
        {
            if (row.PredecessorIndex is not int predecessor) continue;
            var pred = _activities[predecessor];
            var x1 = 12 + (pred.StartDay + pred.DurationDays) * _dayWidth;
            var y1 = HeaderHeight + predecessor * RowHeight + RowHeight / 2;
            var x2 = 12 + row.StartDay * _dayWidth;
            var y2 = HeaderHeight + _activities.IndexOf(row) * RowHeight + RowHeight / 2;
            AddLine(_timeline, x1, y1, x2, y1, "#94A3B8", 1.3);
            AddLine(_timeline, x2, y1, x2, y2, "#94A3B8", 1.3);
        }

        for (var i = 0; i < _activities.Count; i++)
        {
            var row = _activities[i];
            var y = HeaderHeight + i * RowHeight;
            AddLine(_timeline, 0, y + RowHeight, width, y + RowHeight, "#E8EDF3", 1);
            var rowPanel = new Grid { Height = RowHeight, ColumnDefinitions = new ColumnDefinitions("Auto,*"), Background = i % 2 == 0 ? Brushes.White : Brush("#FBFCFE") };
            var code = new TextBlock { Text = row.Code, Width = 66, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(14, 0, 4, 0), Foreground = Brush("#64748B"), FontSize = 11 };
            rowPanel.Children.Add(code);
            var name = new TextBlock { Text = row.Name, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0), Foreground = Brush("#1E293B"), FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis };
            Grid.SetColumn(name, 1); rowPanel.Children.Add(name);
            rowPanel.PointerPressed += (_, _) => ShowActivity(row);
            _taskRows.Children.Add(rowPanel);

            var barX = 12 + row.StartDay * _dayWidth;
            var barY = y + 13;
            if (row.BaselineStartDay.HasValue)
                AddRect(_timeline, 12 + row.BaselineStartDay.Value * _dayWidth, y + 7, row.BaselineDurationDays * _dayWidth, 6, "#CBD5E1", 3);
            var barColor = row.IsCritical ? "#D9485F" : "#3678C8";
            AddRect(_timeline, barX, barY, Math.Max(8, row.DurationDays * _dayWidth), 22, barColor, 5);
            AddRect(_timeline, barX, barY, Math.Max(0, row.DurationDays * _dayWidth * (double)row.PercentComplete / 100d), 22, row.IsCritical ? "#A92E43" : "#20599A", 5);
            AddText(_timeline, $"{row.PercentComplete:0}%", barX + 5, barY + 3, 10, "#FFFFFF", true);
            var hit = new Border { Width = Math.Max(14, row.DurationDays * _dayWidth), Height = 30, Background = Brushes.Transparent, Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand) };
            Canvas.SetLeft(hit, barX); Canvas.SetTop(hit, barY - 4); hit.PointerPressed += (_, _) => ShowActivity(row); _timeline.Children.Add(hit);
        }
    }

    private void ShowActivity(GanttRow row)
    {
        _selection.Text = $"{row.Code}  |  {row.Name}    •    Duration: {row.DurationDays} days    •    Progress: {row.PercentComplete:0}%    •    " +
            $"{(row.IsCritical ? "CRITICAL PATH" : "Non-critical")}    •    Planned start: {new DateOnly(2026, 11, 2).AddDays(row.StartDay):dd MMM yyyy}. " +
            "Dates shown here are illustrative preview values, not persisted project data.";
    }

    private static List<GanttRow> CreateSample() =>
    [
        new("A.100", "Site preparation", 0, 4, 100, false, null, 0, 4),
        new("A.110", "Earthworks", 4, 6, 85, false, 0, 4, 6),
        new("A.120", "Foundations", 10, 7, 45, true, 1, 9, 7),
        new("A.130", "Underground services", 12, 5, 20, false, 1, 12, 5),
        new("A.140", "Structural frame", 19, 8, 0, true, 2, 17, 8),
        new("A.150", "Building envelope", 27, 6, 0, false, 4, 25, 6),
        new("A.160", "MEP installation", 25, 7, 0, false, 3, 23, 7),
        new("A.170", "Testing & handover", 33, 2, 0, true, 5, 31, 2)
    ];

    private static void AddRect(Canvas canvas, double x, double y, double width, double height, string color, double radius = 0)
    {
        var rect = new Rectangle { Width = width, Height = height, Fill = Brush(color), RadiusX = radius, RadiusY = radius };
        Canvas.SetLeft(rect, x); Canvas.SetTop(rect, y); canvas.Children.Add(rect);
    }
    private static void AddLine(Canvas canvas, double x1, double y1, double x2, double y2, string color, double thickness)
    {
        var line = new Line { StartPoint = new Point(x1, y1), EndPoint = new Point(x2, y2), Stroke = Brush(color), StrokeThickness = thickness };
        canvas.Children.Add(line);
    }
    private static void AddText(Canvas canvas, string value, double x, double y, double size, string color, bool bold = false)
    {
        var label = new TextBlock { Text = value, FontSize = size, Foreground = Brush(color), FontWeight = bold ? FontWeight.Bold : FontWeight.Normal };
        Canvas.SetLeft(label, x); Canvas.SetTop(label, y); canvas.Children.Add(label);
    }
    private static IBrush Brush(string value) => new SolidColorBrush(Color.Parse(value));

    private sealed record GanttRow(string Code, string Name, int StartDay, int DurationDays, decimal PercentComplete,
        bool IsCritical, int? PredecessorIndex, int? BaselineStartDay, int BaselineDurationDays);
}
