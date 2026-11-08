namespace CodexUsageMonitor;

/// <summary>Starts the single-instance tray application.</summary>
internal static class Program
{
    /// <summary>Initializes Windows Forms and runs the tray context when no instance exists in the current session.</summary>
    [STAThread]
    private static void Main()
    {
        using var instance = new Mutex(true, @"Local\CodexUsageMonitor", out var isFirstInstance);
        if (!isFirstInstance)
        {
            return;
        }

        ApplicationConfiguration.Initialize();
        using var context = new TrayApplicationContext();
        Application.Run(context);
    }
}
