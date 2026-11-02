using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Projex.ServerManager.Pages;

namespace Projex.ServerManager;

/// <summary>
/// النافذة الرئيسية: شريط جانبي للتنقل + صفحات (الحالة، التثبيت، العملاء، الإعدادات).
/// الواجهة عربية من اليمين لليسار.
/// </summary>
public sealed class MainWindow : Window
{
    private readonly ServerSettings _settings;
    private readonly ServerProbe _probe = new();
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(10) };
    private readonly ContentControl _host = new();
    private readonly DashboardPage _dashboard = new();
    private readonly DatabasePage _database;
    private readonly ClientsPage _clients;
    private readonly SettingsPage _settingsPage;
    private readonly List<(Button Button, Control Page)> _navigation = new();
    private bool _refreshing;

    public MainWindow()
    {
        ServerPaths.EnsureFolders();
        _settings = ServerSettings.Load();
        _database = new DatabasePage(_settings);
        _clients = new ClientsPage(_settings);
        _settingsPage = new SettingsPage(_settings);

        Title = "PROJEX — لوحة تحكم السيرفر";
        Width = 1120;
        Height = 780;
        MinWidth = 960;
        MinHeight = 640;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = Ui.Brush(Ui.Page);

        _dashboard.RefreshRequested += RefreshAsync;
        _database.RefreshRequested += RefreshAsync;

        var root = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("250,*"),
            FlowDirection = FlowDirection.RightToLeft
        };
        var sidebar = BuildSidebar();
        Grid.SetColumn(sidebar, 0);
        root.Children.Add(sidebar);

        var content = new ScrollViewer
        {
            Content = _host,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto
        };
        Grid.SetColumn(content, 1);
        root.Children.Add(content);
        Content = root;

        Navigate(0);

        _timer.Tick += async (_, _) => await RefreshAsync();
        Closed += (_, _) => { _timer.Stop(); _probe.Dispose(); };
        Opened += async (_, _) =>
        {
            _timer.Start();
            await RefreshAsync();
        };
    }

    private Control BuildSidebar()
    {
        var menu = new StackPanel { Spacing = 6, Margin = new Thickness(14, 10) };
        AddNav(menu, "الحالة", _dashboard);
        AddNav(menu, "قاعدة البيانات والتثبيت", _database);
        AddNav(menu, "العملاء", _clients);
        AddNav(menu, "الإعدادات", _settingsPage);

        var brand = new StackPanel
        {
            Margin = new Thickness(22, 26, 22, 18),
            Spacing = 3,
            Children =
            {
                new TextBlock { Text = "PROJEX", FontSize = 26, FontWeight = FontWeight.Bold, Foreground = Brushes.White, FlowDirection = FlowDirection.LeftToRight },
                new TextBlock { Text = "لوحة تحكم السيرفر", FontSize = 13, Foreground = Ui.Brush("#9FB6D2") }
            }
        };

        var footer = new TextBlock
        {
            Text = "الإصدار 0.9.2",
            FontSize = 11,
            Foreground = Ui.Brush("#6F89AA"),
            Margin = new Thickness(22, 0, 22, 18),
            VerticalAlignment = VerticalAlignment.Bottom
        };

        var layout = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(brand, Dock.Top);
        DockPanel.SetDock(footer, Dock.Bottom);
        layout.Children.Add(brand);
        layout.Children.Add(footer);
        layout.Children.Add(menu);

        return new Border { Background = Ui.Brush(Ui.Navy), Child = layout };
    }

    private void AddNav(StackPanel menu, string label, Control page)
    {
        var button = new Button
        {
            Content = label,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Left,
            MinHeight = 44,
            Padding = new Thickness(16, 8),
            FontSize = 14,
            Foreground = Brushes.White,
            Background = Brushes.Transparent,
            CornerRadius = new CornerRadius(8)
        };
        var index = _navigation.Count;
        button.Click += (_, _) => Navigate(index);
        _navigation.Add((button, page));
        menu.Children.Add(button);
    }

    private void Navigate(int index)
    {
        _host.Content = _navigation[index].Page;
        for (var i = 0; i < _navigation.Count; i++)
            _navigation[i].Button.Background = i == index ? Ui.Brush(Ui.NavyLight) : Brushes.Transparent;
    }

    private async Task RefreshAsync()
    {
        if (_refreshing) return;
        _refreshing = true;
        try
        {
            var snapshot = await _probe.ReadAsync(_settings.ApiUri);
            _dashboard.Update(snapshot);
            _database.Update(snapshot);
        }
        catch
        {
            // الفحص التالي بعد 10 ثوانٍ سيحاول مرة أخرى
        }
        finally { _refreshing = false; }
    }
}
