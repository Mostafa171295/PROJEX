using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace Projex.ServerManager.Pages;

/// <summary>
/// صفحة العملاء: عنوان السيرفر الذي يكتبه العملاء، ومجلد Clients الجاهز للنسخ على أجهزتهم،
/// وتفعيل/تعطيل الوصول الآمن من الشبكة.
/// </summary>
internal sealed class ClientsPage : UserControl
{
    private readonly ServerSettings _settings;
    private readonly TextBox _address = new() { MinHeight = 42, Watermark = "https://projex.company.com", FlowDirection = FlowDirection.LeftToRight };
    private readonly TextBlock _message = Ui.Body("");
    private readonly TextBlock _folderStatus = Ui.Body("");

    public ClientsPage(ServerSettings settings)
    {
        _settings = settings;
        _address.Text = settings.PublicServerAddress;

        var addressCard = Ui.Card(new StackPanel
        {
            Spacing = 10,
            Children =
            {
                Ui.SectionTitle("عنوان السيرفر للعملاء"),
                Ui.Body("اكتب العنوان الذي سيتصل به العملاء (يجب أن يبدأ بـ https، أو http://127.0.0.1 للتجربة على نفس الجهاز). سيُحفظ في مجلد العملاء ليُضبط تلقائيًا عند تثبيت برنامج العميل."),
                _address,
                Ui.Buttons(
                    Ui.Primary("حفظ وتجهيز مجلد العملاء", Save),
                    Ui.Secondary("نسخ العنوان", async () => await CopyAsync())),
                _message
            }
        });

        var folderCard = Ui.Card(new StackPanel
        {
            Spacing = 10,
            Children =
            {
                Ui.SectionTitle("مجلد العملاء"),
                Ui.Body("هذا المجلد منفصل عن السيرفر، وفيه برنامج العميل وملف التثبيت. انسخه كاملًا إلى جهاز العميل."),
                _folderStatus,
                Ui.Buttons(
                    Ui.Secondary("فتح مجلد العملاء", () => Ui.OpenPath(ServerPaths.Clients, text => _message.Text = text)),
                    Ui.Secondary("فتح مجلد برنامج العميل", () => Ui.OpenPath(ServerPaths.ClientsDesktop, text => _message.Text = text)))
            }
        });

        var stepsCard = Ui.Card(new StackPanel
        {
            Spacing = 8,
            Children =
            {
                Ui.SectionTitle("تثبيت العميل على أي جهاز (3 خطوات)"),
                Ui.Body("1) انسخ مجلد Clients كاملًا إلى جهاز العميل."),
                Ui.Body("2) اضغط كليك يمين على Setup-Client.bat ثم «تشغيل كمسؤول»."),
                Ui.Body("3) افتح PROJEX من سطح المكتب، وسجّل الدخول. العنوان سيكون مكتوبًا جاهزًا.")
            }
        });

        var lanCard = Ui.Card(new StackPanel
        {
            Spacing = 10,
            Children =
            {
                Ui.SectionTitle("الوصول الآمن من الشبكة"),
                Ui.Body("بشكل افتراضي السيرفر يقبل الاتصال من نفس الجهاز فقط. لتسمح لأجهزة العملاء بالاتصال فعّل الوصول الآمن (يحتاج شهادة HTTPS). لا تفتح منفذ قاعدة البيانات 5432 أبدًا."),
                Ui.Buttons(
                    Ui.Secondary("تفعيل الوصول الآمن", () => Launch("Enable-PROJEX-RemoteAccess.ps1")),
                    Ui.Secondary("تعطيل الوصول الآمن", () => Launch("Disable-PROJEX-RemoteAccess.ps1")))
            }
        });

        Content = new StackPanel
        {
            Margin = new Thickness(32),
            Spacing = 18,
            Children =
            {
                Ui.PageHeader("العملاء", "إدارة العملاء", "جهّز برنامج العميل واكتب عنوان السيرفر مرة واحدة، وكل العملاء يتصلون به."),
                addressCard, folderCard, stepsCard, lanCard
            }
        };
        UpdateFolderStatus();
    }

    private void UpdateFolderStatus()
    {
        var exe = Path.Combine(ServerPaths.ClientsDesktop, "Projex.Desktop.exe");
        var bat = Path.Combine(ServerPaths.Clients, "Setup-Client.bat");
        _folderStatus.Text =
            (File.Exists(exe) ? "✔ برنامج العميل موجود." : "✘ برنامج العميل غير موجود في: " + ServerPaths.ClientsDesktop) + "\n" +
            (File.Exists(bat) ? "✔ ملف Setup-Client.bat موجود." : "✘ ملف Setup-Client.bat غير موجود.");
    }

    private static bool IsValidAddress(string? value, out Uri address)
    {
        address = null!;
        if (!Uri.TryCreate(value?.Trim(), UriKind.Absolute, out var parsed)) return false;
        var secure = parsed.Scheme == Uri.UriSchemeHttps;
        var localHttp = parsed.Scheme == Uri.UriSchemeHttp && parsed.IsLoopback;
        if ((!secure && !localHttp) || !string.IsNullOrEmpty(parsed.UserInfo)
            || !string.IsNullOrEmpty(parsed.Query) || !string.IsNullOrEmpty(parsed.Fragment)) return false;
        address = new Uri(parsed.GetLeftPart(UriPartial.Path).TrimEnd('/') + "/");
        return true;
    }

    private void Save()
    {
        if (!IsValidAddress(_address.Text, out var address))
        {
            _message.Foreground = Ui.Brush(Ui.Red);
            _message.Text = "العنوان غير صالح. استخدم https:// للعناوين البعيدة (أو http://127.0.0.1 للتجربة المحلية)، بدون كلمات سر أو علامة ؟";
            return;
        }
        try
        {
            _settings.PublicServerAddress = address.ToString();
            _settings.Save();
            Directory.CreateDirectory(ServerPaths.Clients);
            File.WriteAllText(ServerPaths.ClientConnectionFile,
                JsonSerializer.Serialize(new { ServerUrl = address.ToString() }, new JsonSerializerOptions { WriteIndented = true }));
            _address.Text = address.ToString();
            _message.Foreground = Ui.Brush(Ui.Green);
            _message.Text = "✔ تم الحفظ. أي جهاز عميل يثبّت من مجلد Clients سيتصل بهذا العنوان تلقائيًا.";
        }
        catch (Exception ex)
        {
            _message.Foreground = Ui.Brush(Ui.Red);
            _message.Text = "تعذّر الحفظ: " + ex.Message;
        }
        UpdateFolderStatus();
    }

    private async Task CopyAsync()
    {
        try
        {
            var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
            if (clipboard is null || string.IsNullOrWhiteSpace(_address.Text)) return;
            await clipboard.SetTextAsync(_address.Text.Trim());
            _message.Foreground = Ui.Brush(Ui.Muted);
            _message.Text = "تم نسخ العنوان.";
        }
        catch (Exception ex) { _message.Text = "تعذّر النسخ: " + ex.Message; }
    }

    private void Launch(string fileName)
    {
        var script = ServerPaths.ScriptPath(fileName);
        if (!File.Exists(script))
        {
            _message.Foreground = Ui.Brush(Ui.Red);
            _message.Text = "ملف الإعداد غير موجود: " + script;
            return;
        }
        try { ScriptRunner.LaunchVisibleElevated(script); }
        catch (Exception ex) { _message.Text = "تعذّر التشغيل: " + ex.Message; }
    }
}
