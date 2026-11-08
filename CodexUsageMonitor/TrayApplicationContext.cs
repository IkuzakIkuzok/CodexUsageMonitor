using CodexUsageMonitor.Core.Configuration;
using CodexUsageMonitor.Core.Models;
using CodexUsageMonitor.Core.Notifications;
using CodexUsageMonitor.Core.Presentation;
using CodexUsageMonitor.Core.Services;

namespace CodexUsageMonitor;

/// <summary>Owns the tray UI, polling cycle, notifications, and optional usage details window.</summary>
/// <remarks>UI updates and threshold observations run on the Windows Forms application thread.</remarks>
internal sealed class TrayApplicationContext : ApplicationContext
{
    private readonly IRateLimitClient client = new CodexRateLimitClient();
    private readonly CancellationTokenSource lifetime = new();
    private readonly MonitorSettings settings;
    private readonly UsageThresholdMonitor thresholdMonitor;
    private readonly System.Windows.Forms.Timer refreshTimer = new();
    private readonly System.Windows.Forms.Timer countdownTimer = new() { Interval = 60_000 };
    private readonly ToolStripMenuItem statusItem = new("Checking usage...") { Enabled = false };
    private readonly ToolStripMenuItem refreshItem = new("Refresh now");
    private readonly ContextMenuStrip menu = new();
    private Icon currentIcon;
    private readonly NotifyIcon trayIcon;
    private UsageIconAppearance iconAppearance = new(new(UsageIconState.Unknown, null), new(UsageIconState.Unknown, null));
    private UsageSnapshot? latest;
    private string? lastError;
    private bool refreshing;
    private bool stopping;
    private bool resourcesDisposed;
    private Form? detailsWindow;
    private TextBox? detailsText;

    /// <summary>Loads startup settings, creates the tray UI, and schedules the initial usage retrieval.</summary>
    public TrayApplicationContext()
    {
        this.settings = MonitorSettings.Load();
        this.thresholdMonitor = new UsageThresholdMonitor(this.settings.NotificationThresholdsPercent);
        this.refreshTimer.Interval = this.settings.RefreshIntervalMilliseconds;
        this.currentIcon = TrayIconFactory.Create(this.iconAppearance, this.settings.IconColors);
        this.menu.Items.Add(new ToolStripMenuItem("Codex Usage Monitor") { Enabled = false });
        this.menu.Items.Add(this.statusItem);
        this.menu.Items.Add(new ToolStripSeparator());
        this.menu.Items.Add(this.refreshItem);
        this.menu.Items.Add("Usage details...", null, (_, _) => ShowDetails());
        this.menu.Items.Add(new ToolStripSeparator());
        this.menu.Items.Add("Exit", null, (_, _) => ExitThread());
        this.refreshItem.Click += async (_, _) => await RefreshAsync();
        this.trayIcon = new NotifyIcon
        {
            Icon = this.currentIcon,
            Text = UsageTextFormatter.Tooltip(null),
            ContextMenuStrip = this.menu,
            Visible = true
        };
        this.trayIcon.DoubleClick += (_, _) => ShowDetails();
        this.trayIcon.BalloonTipClicked += (_, _) => ShowDetails();
        this.refreshTimer.Tick += async (_, _) => await RefreshAsync();
        this.countdownTimer.Tick += (_, _) =>
        {
            if (!this.stopping && this.latest is not null) { UpdateUsageText(); }
        };
        Application.Idle += OnFirstIdle;
    }

    /// <summary>Starts polling and countdown updates, then retrieves usage once the message loop is idle.</summary>
    /// <param name="sender">The idle event source.</param>
    /// <param name="args">The idle event arguments.</param>
    private async void OnFirstIdle(object? sender, EventArgs args)
    {
        Application.Idle -= OnFirstIdle;
        this.refreshTimer.Start();
        this.countdownTimer.Start();
        await RefreshAsync();
    }

    /// <summary>Retrieves usage, updates the UI, and evaluates notifications after a successful reading.</summary>
    /// <returns>A task that completes after the refresh is handled or skipped.</returns>
    /// <remarks>Overlapping refreshes are skipped. Retrieval failures preserve the last successful snapshot.</remarks>
    private async Task RefreshAsync()
    {
        if (this.refreshing || this.stopping) { return; }
        this.refreshing = true;
        this.refreshItem.Enabled = false;
        this.statusItem.Text = "Checking usage...";
        try
        {
            var snapshot = await this.client.ReadAsync(this.lifetime.Token);
            if (this.stopping) { return; }
            this.latest = snapshot;
            this.lastError = null;
        }
        catch (OperationCanceledException) when (this.lifetime.IsCancellationRequested) { return; }
        catch (Exception exception)
        {
            if (this.stopping) { return; }
            this.lastError = exception.Message;
        }
        finally
        {
            this.refreshing = false;
            if (!this.stopping) { this.refreshItem.Enabled = true; }
        }
        UpdateDisplay();
        if (this.lastError is null && this.latest is not null)
        {
            ShowThresholdNotification(this.latest);
        }
    }

    /// <summary>Combines newly reached thresholds into a single native tray notification.</summary>
    /// <param name="snapshot">The successful reading to evaluate for threshold crossings.</param>
    private void ShowThresholdNotification(UsageSnapshot snapshot)
    {
        var alerts = this.thresholdMonitor.Observe(snapshot);
        if (alerts.IsEmpty) { return; }

        try
        {
            // Windows renders tray notifications as native banners. Combine
            // crossings so consecutive calls do not replace each other's text.
            this.trayIcon.ShowBalloonTip(10_000, "Codex usage threshold reached",
                UsageTextFormatter.ThresholdNotification(alerts), ToolTipIcon.Warning);
        }
        catch (System.ComponentModel.Win32Exception exception)
        {
            // A notification failure must not invalidate a successful reading.
            System.Diagnostics.Debug.WriteLine($"Could not display usage notification: {exception.Message}");
        }
    }

    /// <summary>Refreshes the tooltip, status, icon, and any open details view from the cached reading.</summary>
    /// <remarks>Replaced icons are disposed only after both UI consumers have switched to the new icon.</remarks>
    private void UpdateDisplay()
    {
        UpdateUsageText();
        var nextAppearance = UsageIconClassifier.Describe(this.latest, this.settings.IconThresholdsPercent, this.lastError is not null);
        if (nextAppearance != this.iconAppearance)
        {
            var nextIcon = TrayIconFactory.Create(nextAppearance, this.settings.IconColors);
            var previousIcon = this.currentIcon;
            this.trayIcon.Icon = nextIcon;
            this.detailsWindow?.Icon = nextIcon;
            this.currentIcon = nextIcon;
            this.iconAppearance = nextAppearance;
            // Keep only the active icon. Identical readings reuse it, and both
            // UI consumers switch before the previous native handle is released.
            previousIcon.Dispose();
        }
        this.statusItem.Text = this.lastError is not null ? $"Refresh failed — retrying every {this.settings.RefreshIntervalSeconds} seconds"
            : $"Updated {this.latest!.RetrievedAt.ToLocalTime():HH:mm:ss}";
    }

    /// <summary>Updates reset countdowns from the current clock without fetching usage or changing its retrieval status.</summary>
    private void UpdateUsageText()
    {
        var now = DateTimeOffset.UtcNow;
        this.trayIcon.Text = UsageTextFormatter.Tooltip(this.latest, this.lastError, now: now);
        this.detailsText?.Text = UsageTextFormatter.Details(this.latest, this.lastError,
            refreshIntervalSeconds: this.settings.RefreshIntervalSeconds, now: now);
    }

    /// <summary>Activates the existing details window or creates a modeless view of the cached usage.</summary>
    private void ShowDetails()
    {
        if (this.detailsWindow is not null)
        {
            this.detailsWindow.WindowState = FormWindowState.Normal;
            this.detailsWindow.Activate();
            return;
        }
        this.detailsText = new TextBox
        {
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            Dock = DockStyle.Fill,
            BorderStyle = BorderStyle.None,
            BackColor = SystemColors.Window,
            Text = UsageTextFormatter.Details(this.latest, this.lastError, refreshIntervalSeconds: this.settings.RefreshIntervalSeconds)
        };
        this.detailsWindow = new Form
        {
            Text = "Codex Usage Monitor — Usage details",
            Icon = this.currentIcon,
            ClientSize = new Size(530, 440),
            MinimumSize = new Size(380, 280),
            Padding = new Padding(16),
            StartPosition = FormStartPosition.CenterScreen,
            BackColor = SystemColors.Window
        };
        this.detailsWindow.Controls.Add(this.detailsText);
        this.detailsWindow.FormClosed += (_, _) => { this.detailsWindow = null; this.detailsText = null; };
        this.detailsWindow.Show();
        this.detailsText.Select(0, 0);
    }

    /// <summary>Stops polling, cancels retrieval, and closes the tray UI before ending the message loop.</summary>
    protected override void ExitThreadCore()
    {
        if (!this.stopping)
        {
            this.stopping = true;
            this.refreshTimer.Stop();
            this.countdownTimer.Stop();
            this.lifetime.Cancel();
            this.trayIcon.Visible = false;
            this.detailsWindow?.Close();
        }
        base.ExitThreadCore();
    }

    /// <summary>Releases the owned UI resources, client process, and cancellation source.</summary>
    /// <param name="disposing">Whether managed resources should be released.</param>
    protected override void Dispose(bool disposing)
    {
        if (disposing && !this.resourcesDisposed)
        {
            this.resourcesDisposed = true;
            this.stopping = true;
            Application.Idle -= OnFirstIdle;
            this.lifetime.Cancel();
            this.refreshTimer.Dispose();
            this.countdownTimer.Dispose();
            this.trayIcon.Dispose();
            this.detailsWindow?.Dispose();
            this.menu.Dispose();
            // Client awaits never capture the UI context, so shutdown can safely
            // join the cancelled read and release the owned child process here.
            this.client.DisposeAsync().AsTask().GetAwaiter().GetResult();
            this.currentIcon.Dispose();
            this.lifetime.Dispose();
        }
        base.Dispose(disposing);
    }
}
