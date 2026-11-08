using System.Diagnostics;

namespace CodexUsageMonitor.Core.Services;

/// <summary>Locates the Codex executable and prepares a hidden app-server process.</summary>
internal static class CodexCommand
{
    /// <summary>Resolves the configured Codex path or searches PATH and standard installation folders.</summary>
    /// <returns>Start information for a hidden app-server with redirected UTF-8 streams.</returns>
    /// <remarks>The <c>CODEX_USAGE_MONITOR_CODEX_PATH</c> environment variable overrides executable discovery.</remarks>
    /// <exception cref="FileNotFoundException">The configured executable does not exist or Codex cannot be found.</exception>
    /// <exception cref="InvalidOperationException">A command shim path contains unsupported shell expansion characters.</exception>
    public static ProcessStartInfo CreateStartInfo()
    {
        var configuredPath = Environment.GetEnvironmentVariable("CODEX_USAGE_MONITOR_CODEX_PATH");
        string? executable;
        if (!string.IsNullOrWhiteSpace(configuredPath))
        {
            executable = Path.GetFullPath(configuredPath);
            if (!File.Exists(executable))
            {
                throw new FileNotFoundException("CODEX_USAGE_MONITOR_CODEX_PATH does not point to a Codex executable.");
            }
        }
        else
        {
            var folders = (Environment.GetEnvironmentVariable("PATH") ?? "")
                .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
                .Select(folder => folder.Trim().Trim('"'))
                .Concat([
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "OpenAI", "Codex", "bin"),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "npm")
                ]);
            executable = folders.SelectMany(folder => new[]
                { Path.Combine(folder, "codex.exe"), Path.Combine(folder, "codex.cmd") })
                .FirstOrDefault(File.Exists);
        }
        if (executable is null)
        {
            throw new FileNotFoundException("Codex was not found. Install the Codex CLI or set CODEX_USAGE_MONITOR_CODEX_PATH, then restart the monitor.");
        }
        var info = new ProcessStartInfo
        {
            FileName = executable,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardInputEncoding = new System.Text.UTF8Encoding(false),
            StandardOutputEncoding = System.Text.Encoding.UTF8,
            StandardErrorEncoding = System.Text.Encoding.UTF8,
            WorkingDirectory = AppContext.BaseDirectory
        };
        if (Path.GetExtension(executable).Equals(".cmd", StringComparison.OrdinalIgnoreCase))
        {
            // npm installs a cmd shim. Reject characters cmd expands inside quotes.
            if (executable.IndexOfAny(['%', '!', '"', '\r', '\n']) >= 0)
            {
                throw new InvalidOperationException("Use a native codex.exe path when the installation path contains shell expansion characters.");
            }
            info.FileName = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "cmd.exe");
            info.Arguments = $"/d /s /c \"\"{executable}\" app-server --listen stdio://\"";
        }
        else
        {
            info.ArgumentList.Add("app-server");
            info.ArgumentList.Add("--listen");
            info.ArgumentList.Add("stdio://");
        }
        return info;
    }
}
