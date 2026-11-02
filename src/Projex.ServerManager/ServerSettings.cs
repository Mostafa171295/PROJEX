using System.Text.Json;

namespace Projex.ServerManager;

/// <summary>إعدادات السيرفر (غير سرية) — تُحفظ في Server\Settings\server.settings.json</summary>
internal sealed class ServerSettings
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    /// <summary>العنوان الداخلي للـ API على نفس الجهاز (للفحص فقط).</summary>
    public string ApiAddress { get; set; } = "http://127.0.0.1:5080/";

    /// <summary>العنوان الذي يكتبه العملاء في برنامجهم (HTTPS).</summary>
    public string PublicServerAddress { get; set; } = "";

    /// <summary>منفذ PostgreSQL (يُستخدم عند التثبيت).</summary>
    public int DatabasePort { get; set; } = 5432;

    public static ServerSettings Load()
    {
        try
        {
            if (File.Exists(ServerPaths.SettingsFile))
            {
                var loaded = JsonSerializer.Deserialize<ServerSettings>(File.ReadAllText(ServerPaths.SettingsFile));
                if (loaded is not null)
                {
                    if (loaded.DatabasePort is < 1 or > 65535) loaded.DatabasePort = 5432;
                    if (string.IsNullOrWhiteSpace(loaded.ApiAddress)) loaded.ApiAddress = "http://127.0.0.1:5080/";
                    return loaded;
                }
            }
        }
        catch { /* ملف تالف: نرجع للإعدادات الافتراضية */ }
        return new ServerSettings();
    }

    public void Save()
    {
        Directory.CreateDirectory(ServerPaths.Settings);
        File.WriteAllText(ServerPaths.SettingsFile, JsonSerializer.Serialize(this, JsonOptions));
    }

    public Uri ApiUri =>
        Uri.TryCreate(ApiAddress, UriKind.Absolute, out var uri) ? uri : new Uri("http://127.0.0.1:5080/");
}
