using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace Projex.ServerManager.Pages;

/// <summary>الصفحة الرئيسية: حالة السيرفر + أزرار التشغيل والإيقاف.</summary>
internal sealed class DashboardPage : UserControl
{
    private readonly TextBlock _apiService;
    private readonly TextBlock _dbService;
    private readonly TextBlock _live;
    private readonly TextBlock _ready;
    private readonly TextBlock _banner = new() { FontSize = 15, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap };
    private readonly Border _bannerBox = new() { CornerRadius = new CornerRadius(12), Padding = new Thickness(18) };
    private readonly TextBlock _message = Ui.Body("");
    private readonly TextBlock _checked = Ui.Body("", "#94A3B8", 11);
    private readonly Button _start = Ui.Secondary("تشغيل السيرفر");
    private readonly Button _stop = Ui.Secondary("إيقاف السيرفر");
    private readonly Button _restart = Ui.Secondary("إعادة التشغيل");

    public event Func<Task>? RefreshRequested;

    public DashboardPage()
    {
        var api = Ui.StatusCard("خدمة PROJEX (API)", "خدمة ويندوز تعمل في الخلفية");
        var db = Ui.StatusCard("قاعدة البيانات PostgreSQL", "المحرك المحلي للبيانات");
        var live = Ui.StatusCard("اتصال السيرفر", "فحص health/live");
        var ready = Ui.StatusCard("جاهزية قاعدة البيانات", "فحص health/ready");
        _apiService = api.Value; _dbService = db.Value; _live = live.Value; _ready = ready.Value;

        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,*"),
            RowDefinitions = new RowDefinitions("Auto,Auto"),
            ColumnSpacing = 14,
            RowSpacing = 14
        };
        Place(grid, api.Card, 0, 0); Place(grid, db.Card, 1, 0);
        Place(grid, live.Card, 0, 1); Place(grid, ready.Card, 1, 1);

        _bannerBox.Child = _banner;

        _start.Click += async (_, _) => await ChangeAsync(ServiceAction.Start, "تشغيل");
        _stop.Click += async (_, _) => await ChangeAsync(ServiceAction.Stop, "إيقاف");
        _restart.Click += async (_, _) => await ChangeAsync(ServiceAction.Restart, "إعادة تشغيل");

        var controls = new StackPanel
        {
            Spacing = 10,
            Children =
            {
                Ui.SectionTitle("التحكم في السيرفر"),
                Ui.Buttons(_start, _stop, _restart, Ui.Secondary("تحديث الآن", async () => await Raise())),
                _message,
                _checked
            }
        };

        Content = new StackPanel
        {
            Margin = new Thickness(32),
            Spacing = 18,
            Children =
            {
                Ui.PageHeader("لوحة التحكم", "حالة السيرفر", "تابع حالة PROJEX وقاعدة البيانات، وشغّل السيرفر أو أوقفه من هنا."),
                _bannerBox,
                grid,
                Ui.Card(controls)
            }
        };
        Update(ServerSnapshot.Empty);
    }

    private static void Place(Grid grid, Control control, int column, int row)
    {
        Grid.SetColumn(control, column);
        Grid.SetRow(control, row);
        grid.Children.Add(control);
    }

    private Task Raise() => RefreshRequested?.Invoke() ?? Task.CompletedTask;

    private async Task ChangeAsync(ServiceAction action, string label)
    {
        SetButtons(false);
        _message.Text = $"جاري {label} خدمة PROJEX…";
        try
        {
            await ServerProbe.ChangeApiServiceAsync(action);
            _message.Text = $"تم {label} الخدمة بنجاح.";
        }
        catch (Exception ex)
        {
            _message.Text = $"تعذّر {label} الخدمة: {ex.Message}";
        }
        await Raise();
    }

    private void SetButtons(bool enabled)
    {
        _start.IsEnabled = enabled; _stop.IsEnabled = enabled; _restart.IsEnabled = enabled;
    }

    public void Update(ServerSnapshot s)
    {
        Ui.SetStatus(_apiService, s.ApiService);
        Ui.SetStatus(_dbService, s.DatabaseService);
        Ui.SetStatus(_live, s.Live);
        Ui.SetStatus(_ready, s.Ready);
        _checked.Text = "آخر فحص: " + s.At.ToString("g");

        _start.IsEnabled = s.ApiInstalled && !s.ApiRunning;
        _stop.IsEnabled = s.ApiRunning;
        _restart.IsEnabled = s.ApiRunning;

        if (s.AllGood)
        {
            _bannerBox.Background = Ui.Brush("#E7F6EC");
            _banner.Foreground = Ui.Brush(Ui.Green);
            _banner.Text = "✔ كل شيء يعمل. السيرفر جاهز لاستقبال العملاء.";
        }
        else if (!s.DatabaseInstalled)
        {
            _bannerBox.Background = Ui.Brush("#FEF3E2");
            _banner.Foreground = Ui.Brush(Ui.Amber);
            _banner.Text = "PostgreSQL غير مثبّت. هذا أول تشغيل: افتح «قاعدة البيانات والتثبيت» ونفّذ الخطوة 1 ثم الخطوة 2.";
        }
        else if (!s.ApiInstalled)
        {
            _bannerBox.Background = Ui.Brush("#FEF3E2");
            _banner.Foreground = Ui.Brush(Ui.Amber);
            _banner.Text = "خدمة PROJEX غير مثبّتة. افتح «قاعدة البيانات والتثبيت» ونفّذ الخطوة 2.";
        }
        else
        {
            _bannerBox.Background = Ui.Brush("#FDECEC");
            _banner.Foreground = Ui.Brush(Ui.Red);
            _banner.Text = !s.DatabaseRunning
                ? "خدمة PostgreSQL ليست «تعمل» (" + Ui.Arabic(s.DatabaseService) + "). شغّلها من خدمات ويندوز ثم أعد الخطوة 2."
                : !s.ApiRunning
                    ? "خدمة PROJEX ليست «تعمل» (" + Ui.Arabic(s.ApiService) + "). اضغط «تشغيل السيرفر» أو أعد الخطوة 2."
                    : "الخدمة تعمل لكن قاعدة البيانات غير جاهزة. أعد الخطوة 2، أو افتح آخر سجل من صفحة «قاعدة البيانات والتثبيت».";
        }
    }
}
