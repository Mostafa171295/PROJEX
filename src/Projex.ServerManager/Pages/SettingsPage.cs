using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace Projex.ServerManager.Pages;

/// <summary>صفحة الإعدادات: مجلدات السيرفر كلها في مكان واحد + منفذ قاعدة البيانات.</summary>
internal sealed class SettingsPage : UserControl
{
    private readonly ServerSettings _settings;
    private readonly TextBox _port = new() { MinHeight = 38, Width = 140, HorizontalAlignment = HorizontalAlignment.Left, FlowDirection = FlowDirection.LeftToRight };
    private readonly TextBlock _message = Ui.Body("");

    public SettingsPage(ServerSettings settings)
    {
        _settings = settings;
        _port.Text = settings.DatabasePort.ToString();

        var folders = new StackPanel { Spacing = 10 };
        folders.Children.Add(Ui.SectionTitle("مجلدات السيرفر"));
        folders.Children.Add(FolderRow("الإعدادات والسجلات", ServerPaths.Settings));
        folders.Children.Add(FolderRow("النسخ الاحتياطية", ServerPaths.Backups));
        folders.Children.Add(FolderRow("ملفات السيرفر (API)", ServerPaths.Api));
        folders.Children.Add(FolderRow("مجلد العملاء", ServerPaths.Clients));
        folders.Children.Add(FolderRow("الأدلة والتوثيق", ServerPaths.Docs));

        var general = Ui.Card(new StackPanel
        {
            Spacing = 10,
            Children =
            {
                Ui.SectionTitle("إعدادات عامة"),
                Ui.Body("عنوان السيرفر الداخلي (للفحص فقط): " + settings.ApiAddress),
                Ui.Body("منفذ قاعدة البيانات PostgreSQL (يُستخدم عند تثبيت PostgreSQL فقط. الافتراضي 5432):"),
                _port,
                Ui.Buttons(
                    Ui.Primary("حفظ الإعدادات", Save),
                    Ui.Secondary("خدمات ويندوز", () => Ui.OpenPath("services.msc", text => _message.Text = text))),
                _message
            }
        });

        var secrets = Ui.Card(new StackPanel
        {
            Spacing = 6,
            Children =
            {
                Ui.SectionTitle("كلمات السر"),
                Ui.Body("كلمات سر قاعدة البيانات تُنشأ تلقائيًا وتُحفظ مشفّرة بواسطة ويندوز (وليس في ملفات نصية) في: " + ServerPaths.Provisioning
                        + "\nلا تنسخ هذا المجلد ولا ترسله لأحد.")
            }
        });

        Content = new StackPanel
        {
            Margin = new Thickness(32),
            Spacing = 18,
            Children =
            {
                Ui.PageHeader("الإعدادات", "الإعدادات والمجلدات", "كل إعدادات السيرفر وسجلاته في مجلد واحد: Server\\Settings"),
                Ui.Card(folders),
                general,
                secrets
            }
        };
    }

    private Control FolderRow(string title, string path)
    {
        var info = new StackPanel
        {
            Spacing = 2,
            Children =
            {
                new TextBlock { Text = title, FontWeight = FontWeight.SemiBold, Foreground = Ui.Brush(Ui.Ink) },
                new TextBlock { Text = path, FontSize = 11, Foreground = Ui.Brush(Ui.Muted), FlowDirection = FlowDirection.LeftToRight, TextWrapping = TextWrapping.Wrap }
            }
        };
        var open = Ui.Secondary("فتح", () => { ServerPaths.EnsureFolders(); Ui.OpenPath(path, text => _message.Text = text); });
        open.MinWidth = 80;
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 12 };
        Grid.SetColumn(info, 0);
        Grid.SetColumn(open, 1);
        grid.Children.Add(info);
        grid.Children.Add(open);
        return grid;
    }

    private void Save()
    {
        if (!int.TryParse(_port.Text, out var port) || port is < 1 or > 65535)
        {
            _message.Foreground = Ui.Brush(Ui.Red);
            _message.Text = "المنفذ يجب أن يكون رقمًا بين 1 و 65535.";
            return;
        }
        try
        {
            _settings.DatabasePort = port;
            _settings.Save();
            _message.Foreground = Ui.Brush(Ui.Green);
            _message.Text = "✔ تم حفظ الإعدادات.";
        }
        catch (Exception ex)
        {
            _message.Foreground = Ui.Brush(Ui.Red);
            _message.Text = "تعذّر الحفظ: " + ex.Message;
        }
    }
}
