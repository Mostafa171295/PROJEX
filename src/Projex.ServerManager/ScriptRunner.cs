using System.Diagnostics;

namespace Projex.ServerManager;

/// <summary>يشغّل سكربت PowerShell مخفيًا ويرسل كل سطر ناتج للشاشة.</summary>
internal static class ScriptRunner
{
    public static async Task<int> RunAsync(
        string scriptPath,
        IEnumerable<string> arguments,
        IDictionary<string, string>? environment,
        Action<string> onLine)
    {
        var info = new ProcessStartInfo("powershell.exe")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        info.ArgumentList.Add("-NoProfile");
        info.ArgumentList.Add("-NonInteractive");
        info.ArgumentList.Add("-ExecutionPolicy");
        info.ArgumentList.Add("Bypass");
        info.ArgumentList.Add("-File");
        info.ArgumentList.Add(scriptPath);
        foreach (var argument in arguments) info.ArgumentList.Add(argument);
        if (environment is not null)
            foreach (var pair in environment) info.Environment[pair.Key] = pair.Value;

        using var process = new Process { StartInfo = info };
        void Handle(string? data)
        {
            if (data is null) return;
            // winget يكتب أشرطة تقدم بحرف \r: نأخذ آخر جزء مفيد فقط
            var parts = data.Split('\r', StringSplitOptions.RemoveEmptyEntries);
            var line = (parts.Length == 0 ? "" : parts[^1]).Trim();
            if (line.Length <= 1) return;
            if (line.All(c => c is '-' or '\\' or '|' or '/' or ' ' or '█' or '▒' or '░')) return;
            onLine(line);
        }
        process.OutputDataReceived += (_, e) => Handle(e.Data);
        process.ErrorDataReceived += (_, e) => Handle(e.Data);

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        await process.WaitForExitAsync();
        return process.ExitCode;
    }

    /// <summary>يفتح سكربت في نافذة PowerShell ظاهرة بصلاحيات المدير (للمعالجات التفاعلية).</summary>
    public static void LaunchVisibleElevated(string scriptPath)
    {
        Process.Start(new ProcessStartInfo("powershell.exe")
        {
            UseShellExecute = true,
            Verb = "runas",
            Arguments = $"-NoProfile -ExecutionPolicy Bypass -File \"{scriptPath}\""
        });
    }
}
