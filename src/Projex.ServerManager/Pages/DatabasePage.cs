using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

namespace Projex.ServerManager.Pages;

/// <summary>
/// صفحة التثبيت: زر لتحميل وتثبيت PostgreSQL تلقائيًا، ثم زر لإنشاء قاعدة البيانات وتشغيل السيرفر.
/// كل الشغل بيتم عبر سكربتات Server\Scripts مخفية، والناتج بيظهر هنا في سجل مباشر.
/// </summary>
internal sealed class DatabasePage : UserControl
{
    private readonly ServerSettings _settings;
    private readonly TextBlock _step1Status = Ui.Body("");
    private readonly TextBlock _step2Status = Ui.Body("");
    private readonly Button _installPostgres = Ui.Primary("تحميل وتثبيت PostgreSQL تلقائيًا");
    private readonly Button _provision = Ui.Primary("إنشاء قاعدة البيانات وتشغيل السيرفر");
    private readonly TextBox _adminPassword = new() { PasswordChar = '●', MinHeight = 38, Watermark = "كلمة سر مستخدم postgres" };
    private readonly Border _adminBox = new();
    private readonly TextBlock _warning = Ui.Body("", Ui.Amber);
    private readonly ProgressBar _progress = new() { IsIndeterminate = true, Height = 6, IsVisible = false };
    private readonly TextBlock _current = new() { FontSize = 14, FontWeight = FontWeight.SemiBold, Foreground = Ui.Brush(Ui.Ink), TextWrapping = TextWrapping.Wrap };
    private readonly TextBox _log = new()
    {
        IsReadOnly = true,
        AcceptsReturn = true,
        TextWrapping = TextWrapping.NoWrap,
        FontFamily = new FontFamily("Consolas, Courier New, monospace"),
        FontSize = 12,
        Height = 260,
        FlowDirection = FlowDirection.LeftToRight
    };
    private readonly List<string> _lines = new();
    private string _logFile = "";
    private string _lastError = "";
    private bool _busy;
    private ServerSnapshot _snapshot = ServerSnapshot.Empty;

    public event Func<Task>? RefreshRequested;

    public DatabasePage(ServerSettings settings)
    {
        _settings = settings;

        _installPostgres.Click += async (_, _) => await RunStageAsync("PostgresOnly", "تحميل وتثبيت PostgreSQL 16");
        _provision.Click += async (_, _) => await RunStageAsync("ProvisionOnly", "إنشاء قاعدة البيانات وتشغيل السيرفر");

        _adminBox.Child = new StackPanel
        {
            Spacing = 6,
            Margin = new Thickness(0, 6, 0, 0),
            Children =
            {
                Ui.Body("PostgreSQL مثبّت من قبل على جهازك، فنحتاج كلمة سر المستخدم postgres مرة واحدة فقط (لن تُحفظ):"),
                _adminPassword
            }
        };

        var step1 = Ui.Card(new StackPanel
        {
            Spacing = 8,
            Children =
            {
                Ui.SectionTitle("الخطوة 1 — تحميل وتثبيت PostgreSQL"),
                Ui.Body("يحمّل PostgreSQL 16 من مصدر ويندوز الرسمي (winget) ويثبّته بدون أي أسئلة. يحتاج إنترنت وقد يأخذ عدة دقائق."),
                _step1Status,
                Ui.Buttons(_installPostgres)
            }
        });

        var step2 = Ui.Card(new StackPanel
        {
            Spacing = 8,
            Children =
            {
                Ui.SectionTitle("الخطوة 2 — إنشاء قاعدة البيانات وتشغيل السيرفر"),
                Ui.Body("ينشئ قاعدة بيانات PROJEX وكلمات السر (تلقائيًا ومشفّرة)، ويجهّز الجداول، ويثبّت خدمة PROJEX ويشغّلها."),
                _step2Status,
                _adminBox,
                Ui.Buttons(_provision)
            }
        });

        var logCard = Ui.Card(new StackPanel
        {
            Spacing = 10,
            Children =
            {
                Ui.SectionTitle("ما يحدث الآن"),
                _current,
                _progress,
                _log,
                Ui.Buttons(
                    Ui.Secondary("فتح آخر سجل", OpenLatestLog),
                    Ui.Secondary("فتح مجلد السجلات", () => Ui.OpenPath(ServerPaths.Logs, Show)),
                    Ui.Secondary("مسح الشاشة", () => { _lines.Clear(); _log.Text = ""; }))
            }
        });

        Content = new StackPanel
        {
            Margin = new Thickness(32),
            Spacing = 18,
            Children =
            {
                Ui.PageHeader("التثبيت", "قاعدة البيانات والتثبيت", "نفّذ الخطوتين بالترتيب. كل خطوة بضغطة زر واحدة."),
                _warning,
                step1,
                step2,
                logCard
            }
        };
        Update(ServerSnapshot.Empty);
    }

    private void Show(string text) => _current.Text = text;

    private void OpenLatestLog()
    {
        try
        {
            var latest = Directory.Exists(ServerPaths.Logs)
                ? Directory.GetFiles(ServerPaths.Logs, "*.log").OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault()
                : null;
            if (latest is null) { Show("لا يوجد سجل بعد. نفّذ إحدى الخطوتين أولًا."); return; }
            Ui.OpenPath(latest, Show);
        }
        catch (Exception ex) { Show("تعذّر فتح السجل: " + ex.Message); }
    }

    public void Update(ServerSnapshot s)
    {
        _snapshot = s;

        if (s.DatabaseRunning)
            _step1Status.Text = "✔ PostgreSQL 16 مثبّت ويعمل.";
        else if (s.DatabaseInstalled)
            _step1Status.Text = "PostgreSQL 16 مثبّت لكنه متوقف. شغّله من خدمات ويندوز ثم حدّث.";
        else
            _step1Status.Text = "PostgreSQL 16 غير مثبّت.";

        if (s.ApiRunning && s.Ready == "Ready")
            _step2Status.Text = "✔ السيرفر مجهّز ويعمل.";
        else if (s.ApiInstalled)
            _step2Status.Text = "خدمة PROJEX مثبّتة لكنها ليست جاهزة بعد. راجع صفحة الحالة.";
        else
            _step2Status.Text = "لم يتم التجهيز بعد.";

        var hasSavedAdminPassword = File.Exists(Path.Combine(ServerPaths.Provisioning, "postgresql16-admin.dpapi"));
        _adminBox.IsVisible = s.DatabaseInstalled && !s.ApiInstalled && !hasSavedAdminPassword;

        if (!s.DatabaseInstalled && !string.IsNullOrWhiteSpace(s.OtherPostgres))
        {
            _warning.Text = $"تنبيه: يوجد إصدار PostgreSQL مختلف على الجهاز ({s.OtherPostgres}). PROJEX يحتاج النسخة 16، ولن يغيّر البرنامج النسخة الموجودة.";
            _warning.IsVisible = true;
        }
        else _warning.IsVisible = false;

        ApplyButtons();
    }

    private void ApplyButtons()
    {
        _installPostgres.IsEnabled = !_busy && !_snapshot.DatabaseInstalled;
        _provision.IsEnabled = !_busy && _snapshot.DatabaseRunning;
    }

    private void Append(string line)
    {
        if (line.StartsWith("ERROR:")) _lastError = line.Substring(6).Trim();
        _lines.Add(line);
        if (_lines.Count > 500) _lines.RemoveRange(0, _lines.Count - 500);
        _log.Text = string.Join(Environment.NewLine, _lines);
        _log.CaretIndex = _log.Text.Length;
        if (!string.IsNullOrEmpty(_logFile))
        {
            try { File.AppendAllText(_logFile, $"[{DateTime.Now:HH:mm:ss}] {line}{Environment.NewLine}"); } catch { /* ignore */ }
        }
    }

    private async Task RunStageAsync(string stage, string title)
    {
        var script = ServerPaths.ScriptPath("Install-PROJEX-Server.ps1");
        if (!File.Exists(script))
        {
            _current.Text = "ملف التثبيت غير موجود: " + script + " — فك ضغط حزمة PROJEX كاملة أولًا.";
            return;
        }

        ServerPaths.EnsureFolders();
        _logFile = Path.Combine(ServerPaths.Logs, $"setup-{DateTime.Now:yyyyMMdd-HHmmss}.log");
        _lastError = "";
        _busy = true;
        ApplyButtons();
        _progress.IsVisible = true;
        _current.Text = title + " … من فضلك انتظر ولا تغلق البرنامج.";
        Append("== " + title + " ==");

        var arguments = new List<string>
        {
            "-Stage", stage,
            "-NonInteractive",
            "-AutoOwnerPassword",
            "-DatabasePort", _settings.DatabasePort.ToString(),
            "-LogDirectory", ServerPaths.Logs
        };
        var environment = new Dictionary<string, string>();
        if (stage == "ProvisionOnly" && !string.IsNullOrEmpty(_adminPassword.Text))
            environment["PROJEX_POSTGRES_ADMIN_PASSWORD"] = _adminPassword.Text;

        int code;
        try
        {
            code = await ScriptRunner.RunAsync(script, arguments, environment,
                line => Dispatcher.UIThread.Post(() => Append(line)));
        }
        catch (Exception ex)
        {
            code = -1;
            Append("ERROR: " + ex.Message);
        }

        _adminPassword.Text = "";
        _busy = false;
        _progress.IsVisible = false;
        if (code == 0)
        {
            _current.Foreground = Ui.Brush(Ui.Green);
            _current.Text = "✔ تمت الخطوة بنجاح: " + title;
        }
        else
        {
            _current.Foreground = Ui.Brush(Ui.Red);
            _current.Text = string.IsNullOrEmpty(_lastError)
                ? "فشلت الخطوة. راجع السجل بالأسفل."
                : "فشلت الخطوة: " + _lastError;
        }
        Append(code == 0 ? "== انتهى بنجاح ==" : $"== فشل (كود {code}) ==");
        if (RefreshRequested is not null) await RefreshRequested();
        ApplyButtons();
    }
}
