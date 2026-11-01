using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;

namespace Projex.Desktop;

/// <summary>Authenticated live schedule view with concurrency-safe duration editing.</summary>
public sealed class LiveGanttWindow : Window
{
    private readonly GanttApiClient _client;
    private GanttScheduleDto _schedule;
    private readonly StackPanel _content = new() { Spacing = 12 };
    private readonly TextBlock _message = new() { TextWrapping = TextWrapping.Wrap };
    private bool _busy;

    public LiveGanttWindow(GanttApiClient client, GanttScheduleDto schedule)
    {
        _client = client;
        _schedule = schedule;
        Title = "PROJEX ENTERPRISE — Live Schedule Editor";
        Width = 1420; Height = 860; MinWidth = 1000; MinHeight = 640;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Content = new ScrollViewer { Content = _content, Margin = new Thickness(18) };
        Closed += (_, _) => _client.Dispose();
        Render();
    }

    private void Render()
    {
        _content.Children.Clear();
        _content.Children.Add(new TextBlock { Text = "Live project schedule", FontSize = 25, FontWeight = FontWeight.Bold, Foreground = Brush("#172B4D") });
        _content.Children.Add(new TextBlock { Text = $"Project: {_schedule.ProjectId}   ·   {_schedule.Activities.Count} activities   ·   {_schedule.Relationships.Count} relationships", Foreground = Brush("#64748B") });
        var toolbar = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        var refresh = new Button { Content = "Refresh from server", MinHeight = 34 };
        refresh.Click += async (_, _) => await ReloadAsync();
        toolbar.Children.Add(refresh);
        toolbar.Children.Add(new TextBlock { Text = "Edit duration in working days (8 hours/day). Changes use RowVersion concurrency checks.", VerticalAlignment = VerticalAlignment.Center, Foreground = Brush("#64748B"), FontSize = 11 });
        _content.Children.Add(toolbar);

        var statusCurrent = _schedule.ScheduleStatus.Equals("Current", StringComparison.OrdinalIgnoreCase);
        var statusColor = statusCurrent && !_schedule.RequiresRecalculation ? "#E7F6EC" : "#FFF3D6";
        var statusText = _schedule.RequiresRecalculation ? $"SCHEDULE {_schedule.ScheduleStatus.ToUpperInvariant()} — Recalculate before relying on dates or float." : $"SCHEDULE {_schedule.ScheduleStatus.ToUpperInvariant()}";
        _content.Children.Add(new Border { Background = Brush(statusColor), CornerRadius = new CornerRadius(8), Padding = new Thickness(14), Child = new TextBlock { Text = statusText, FontWeight = FontWeight.Bold, Foreground = Brush("#654B16") } });
        if (_schedule.Baseline is not null)
            _content.Children.Add(new TextBlock { Text = $"Baseline overlay: {_schedule.Baseline.Name} · Version {_schedule.Baseline.Version} · {_schedule.Baseline.Status}", Foreground = Brush("#475569") });
        else _content.Children.Add(new TextBlock { Text = "No baseline overlay selected.", Foreground = Brush("#64748B") });
        _content.Children.Add(_message);

        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("90,*,95,120,120,85,95,90"), Background = Brush("#EEF2F7"), Height = 42 };
        string[] headings = ["CODE", "ACTIVITY / WBS", "DURATION", "START", "FINISH", "PROGRESS", "FLOAT", "EDIT (wd)"];
        for (var i = 0; i < headings.Length; i++) AddCell(header, headings[i], i, true);
        _content.Children.Add(header);
        var rows = new StackPanel { Spacing = 1 };
        foreach (var activity in _schedule.Activities)
        {
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("90,*,95,120,120,85,95,90"), MinHeight = 58, Background = rows.Children.Count % 2 == 0 ? Brushes.White : Brush("#FAFBFD") };
            AddCell(row, activity.IsCritical ? $"{activity.ActivityCode} ★" : activity.ActivityCode, 0);
            AddCell(row, $"{activity.Name}{Environment.NewLine}{activity.WbsCode} · {activity.WbsName}", 1);
            AddCell(row, $"{activity.DurationMinutes / 480m:0.##} wd", 2);
            AddCell(row, activity.PlannedStart?.ToString("yyyy-MM-dd") ?? "—", 3);
            AddCell(row, activity.PlannedFinish?.ToString("yyyy-MM-dd") ?? "—", 4);
            AddCell(row, $"{activity.PercentComplete:0.#}%", 5);
            AddCell(row, $"{activity.TotalFloatMinutes / 480m:0.##} wd", 6);
            var edit = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(3) };
            var duration = new TextBox { Text = (activity.DurationMinutes / 480m).ToString("0.##", System.Globalization.CultureInfo.InvariantCulture), Width = 52, Height = 30, VerticalContentAlignment = VerticalAlignment.Center };
            var save = new Button { Content = "Save", Padding = new Thickness(6, 3), MinHeight = 30, FontSize = 11 };
            save.Click += async (_, _) => await SaveDurationAsync(activity, duration, save);
            edit.Children.Add(duration); edit.Children.Add(save);
            Grid.SetColumn(edit, 7); row.Children.Add(edit);
            rows.Children.Add(row);
        }
        _content.Children.Add(new ScrollViewer { Content = rows, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MaxHeight = 590 });
        _content.Children.Add(new TextBlock { Text = "Duration edits are sent to the authenticated API. The API marks the schedule Stale; recalculation is required before treating dates, float, or critical path as current.", TextWrapping = TextWrapping.Wrap, Foreground = Brush("#64748B"), FontSize = 11 });
    }

    private async Task SaveDurationAsync(GanttActivityDto activity, TextBox input, Button button)
    {
        if (_busy) return;
        if (!decimal.TryParse(input.Text, System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out var days) || days < 0 || days > int.MaxValue / 480m)
        {
            _message.Text = "Enter a valid non-negative duration in working days, using a dot for decimals.";
            _message.Foreground = Brushes.Firebrick;
            return;
        }
        var minutes = decimal.ToInt32(decimal.Round(days * 480m, 0, MidpointRounding.AwayFromZero));
        _busy = true; button.IsEnabled = false; _message.Text = "Saving duration…"; _message.Foreground = Brush("#475569");
        try
        {
            await _client.UpdateDurationAsync(_schedule.ProjectId, activity.Id, minutes, activity.RowVersion);
            _message.Text = "Duration saved. The schedule is now stale; reloading the server view.";
            _message.Foreground = Brush("#166534");
            _busy = false;
            await ReloadAsync();
        }
        catch (Exception ex)
        {
            _message.Text = ex.Message;
            _message.Foreground = Brushes.Firebrick;
        }
        finally { _busy = false; button.IsEnabled = true; }
    }

    private async Task ReloadAsync()
    {
        if (_busy) return;
        _busy = true;
        try
        {
            _schedule = await _client.GetScheduleAsync(_schedule.ProjectId, _schedule.Baseline?.Id);
            _message.Text = "Schedule refreshed from server.";
            _message.Foreground = Brush("#166534");
            Render();
        }
        catch (Exception ex) { _message.Text = ex.Message; _message.Foreground = Brushes.Firebrick; }
        finally { _busy = false; }
    }

    private static void AddCell(Grid grid, string value, int column, bool heading = false)
    {
        var cell = new TextBlock { Text = value, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 4), FontSize = heading ? 10 : 11, FontWeight = heading ? FontWeight.Bold : FontWeight.Normal, Foreground = heading ? Brush("#475569") : Brush("#1E293B"), TextWrapping = TextWrapping.Wrap };
        Grid.SetColumn(cell, column); grid.Children.Add(cell);
    }
    private static IBrush Brush(string color) => new SolidColorBrush(Color.Parse(color));
}
