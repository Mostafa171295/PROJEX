namespace Projex.ServerManager;

/// <summary>
/// كل مسارات المجلدات في مكان واحد.
/// الشكل المتوقع بعد فك الضغط:
///   PROJEX\Server\Manager   (هذا البرنامج)
///   PROJEX\Server\API, Scripts, Database, Docs
///   PROJEX\Server\Settings  (كل الإعدادات + السجلات)
///   PROJEX\Clients\Desktop  (برنامج العميل)
/// </summary>
internal static class ServerPaths
{
    public static string ServerRoot { get; } =
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, ".."));

    public static string InstallRoot { get; } =
        Path.GetFullPath(Path.Combine(ServerRoot, ".."));

    public static string Scripts => Path.Combine(ServerRoot, "Scripts");
    public static string Api => Path.Combine(ServerRoot, "API");
    public static string Docs => Path.Combine(ServerRoot, "Docs");
    public static string Settings => Path.Combine(ServerRoot, "Settings");
    public static string Logs => Path.Combine(Settings, "Logs");
    public static string SettingsFile => Path.Combine(Settings, "server.settings.json");
    public static string Clients => Path.Combine(InstallRoot, "Clients");
    public static string ClientsDesktop => Path.Combine(Clients, "Desktop");
    public static string ClientConnectionFile => Path.Combine(Clients, "connection.json");

    public static string Backups => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "PROJEX", "Backups");

    public static string Provisioning => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "PROJEX", "Provisioning");

    public static string ScriptPath(string fileName) => Path.Combine(Scripts, fileName);

    public static void EnsureFolders()
    {
        foreach (var folder in new[] { Settings, Logs })
        {
            try { Directory.CreateDirectory(folder); } catch { /* the page shows a message when needed */ }
        }
    }
}
