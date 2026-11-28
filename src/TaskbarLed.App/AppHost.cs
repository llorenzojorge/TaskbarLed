using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Threading;
using TaskbarLed.App.Infrastructure;
using TaskbarLed.App.Overlays;
using TaskbarLed.App.Panels;
using TaskbarLed.App.Presentation;
using TaskbarLed.App.Themes;
using TaskbarLed.App.Tray;
using TaskbarLed.Core.Audio;
using TaskbarLed.Core.Presentation;
using TaskbarLed.Core.Sessions;
using TaskbarLed.Core.Settings;
using TaskbarLed.Core.Sources;
using TaskbarLed.Core.Visibility;
using TaskbarLed.Platform.Windows.Audio;
using TaskbarLed.Platform.Windows.Ipc;
using TaskbarLed.Platform.Windows.System;
using TaskbarLed.Platform.Windows.Windowing;
using TaskbarLed.Sources.ClaudeCode;
using TaskbarLed.Sources.Generic;

namespace TaskbarLed.App;

/// <summary>
/// Composition root and run loop. Wires the pieces by hand ("pure DI"): the graph is small and
/// explicit wiring keeps startup fast and the flow easy to follow for contributors.
/// </summary>
public sealed class AppHost : IDisposable
{
    private static readonly TimeSpan VisibilityInterval = TimeSpan.FromMilliseconds(400);
    private static readonly TimeSpan FrameInterval = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan PruneInterval = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan QuietCheckInterval = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan QuietAfter = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan HintDuration = TimeSpan.FromSeconds(6);
    private static readonly TimeSpan PreviewLinger = TimeSpan.FromSeconds(5);

    private readonly AppLog _log = new();
    private readonly CultureInfo _culture = CultureInfo.CurrentUICulture;
    private readonly UiStrings _strings;
    private readonly SettingsStore _settingsStore;
    private readonly SessionStore _store = new();
    private readonly HookEventServer _server = new();
    private readonly CancellationTokenSource _stop = new();
    private readonly IClaudeVisibilityProbe _probe;
    private readonly ClaudeCodeHookInstaller _hooks;
    private readonly AutoStart _autoStart;
    private readonly PanelViewModel _viewModel;
    private readonly PanelManager _panels;
    private readonly OverlayManager _overlays = new();
    private readonly AlertDetector _alerts;
    private readonly SoundCueSelector _soundCues = new();
    private readonly WavePlayer _sound = new();
    private TaskbarButtonWatcher? _taskbarButtons;
    private readonly DispatcherTimer _visibilityTimer;
    private readonly DispatcherTimer _frameTimer;
    private readonly DispatcherTimer _pruneTimer;
    private readonly DispatcherTimer _quietTimer;

    private AppSettings _settings;
    private PanelComposer _composer;
    private TrayIcon? _tray;
    private Task? _pump;
    private EventPump? _eventPump;
    private int _quietCheckRunning;
    private nint _claudeWindow;
    private HostWindows _hostWindows = HostWindows.None;
    private bool _allInSight;
    private string? _selectedSessionId;
    private HashSet<string> _reportedThemeProblems = [];
    private IReadOnlyList<MonitorInfo> _monitors = [];
    private IReadOnlyList<MonitorInfo> _targets = [];
    private Dictionary<nint, FullscreenKind> _fullscreen = [];
    private PanelSettings? _editOrigin;
    private DispatcherTimer? _hintTimer;
    private bool _menuOpen;
    private DateTimeOffset _previewUntil;
    private int _framePending;
    private bool _soundFailureLogged;
    private bool _disposed;

    /// <param name="probe">Visibility check; replaceable for diagnostics or alternative window rules.</param>
    public AppHost(IClaudeVisibilityProbe? probe = null)
    {
        _probe = probe ?? new ClaudeVisibilityProbe();
        _strings = UiStrings.For(_culture);
        _settingsStore = new SettingsStore(_log);
        _settings = _settingsStore.Load();
        _composer = CreateComposer();
        _alerts = new AlertDetector(TickerStrings.For(_culture), _culture);

        var appPath = Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "TaskbarLed.exe");
        _hooks = new ClaudeCodeHookInstaller(
            ClaudeCodeHookInstaller.DefaultSettingsPath, Path.Combine(AppContext.BaseDirectory, "TaskbarLed.Hook.exe"));
        _autoStart = new AutoStart("TaskbarLed", appPath);

        _viewModel = new PanelViewModel(_strings.EditHint, _culture) { SaveLabel = _strings.Save, CancelLabel = _strings.Cancel };
        _panels = new PanelManager(_viewModel, _settings.Panel);
        _panels.OpenRequested += (_, _) => OpenClaude();
        _panels.TabOpenRequested += (_, id) => OpenTab(id);
        _panels.NextRequested += (_, _) => { _composer.Rotation.Skip(); RenderFrame(); };
        _panels.MenuRequested += (_, _) => _tray?.ShowMenuAtCursor();
        _panels.SettingsEdited += (_, panel) => _settings = _settings with { Panel = panel };
        _panels.EditFinished += (_, save) => FinishEditing(save);
        _overlays.OpenRequested += (_, _) => OpenClaude();

        _visibilityTimer = new DispatcherTimer(VisibilityInterval, DispatcherPriority.Background, (_, _) => UpdateVisibility(), Dispatcher.CurrentDispatcher);
        _frameTimer = new DispatcherTimer(FrameInterval, DispatcherPriority.Background, (_, _) => RenderFrame(), Dispatcher.CurrentDispatcher);
        _pruneTimer = new DispatcherTimer(PruneInterval, DispatcherPriority.Background, (_, _) => Prune(), Dispatcher.CurrentDispatcher);
        _quietTimer = new DispatcherTimer(QuietCheckInterval, DispatcherPriority.Background, (_, _) => CheckQuietSessions(), Dispatcher.CurrentDispatcher);
    }

    public void Start()
    {
        _log.Info("Starting.");
        LoadCustomThemes();
        ApplyTheme(_settings.Panel.Theme);
        _tray = new TrayIcon(_strings, TrayState, CreateTrayCommands());
        _tray.MenuVisibilityChanged += (_, open) => OnMenuVisibilityChanged(open);

        EnsureAutoStart();
        EnsureClaudeCodeHooks();
        _taskbarButtons = new TaskbarButtonWatcher(ex => _log.Error("Could not read the taskbar buttons.", ex));

        _store.Changed += (_, _) => ScheduleFrame();
        _server.Faulted += (_, ex) => _log.Error("IPC listener fault.", ex);
        _server.Start();

        ISessionSource[] sources =
        [
            new ClaudeCodeSource(
                new ToolActivityFormatter(ActivityStrings.For(_culture), _culture)),
            new GenericAgentSource(),
        ];
        var pump = new EventPump(sources, _store, _log);
        _eventPump = pump;
        _pump = Task.Run(() => pump.RunAsync(_server.Envelopes, _stop.Token));

        RenderFrame();
        UpdateVisibility();
        _visibilityTimer.Start();
        _frameTimer.Start();
        _pruneTimer.Start();
        _quietTimer.Start();
    }

    /// <summary>
    /// Stops everything synchronously on the UI thread. Never waits on async work: it runs from
    /// Application.OnExit, where blocking on a task that needs the UI thread would freeze the app.
    /// The pump and pipe listeners only need their token cancelled; the process is about to end.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _visibilityTimer.Stop();
        _frameTimer.Stop();
        _pruneTimer.Stop();
        _quietTimer.Stop();
        _hintTimer?.Stop();
        _stop.Cancel();
        _server.Stop();

        _taskbarButtons?.Dispose();
        _panels.Dispose();
        _overlays.Dispose();
        _sound.Dispose();
        _tray?.Dispose();
        _stop.Dispose();
        _log.Info("Stopped.");
    }

    private PanelComposer CreateComposer() => new(
        new SessionRotation(TimeSpan.FromSeconds(_settings.Panel.RotationSeconds)),
        TickerStrings.For(_culture),
        _culture);

    // Store events arrive on the pump thread; coalesce them into one render on the UI thread.
    private void ScheduleFrame()
    {
        if (Interlocked.Exchange(ref _framePending, 1) == 0)
        {
            Application.Current?.Dispatcher.BeginInvoke(() =>
            {
                Interlocked.Exchange(ref _framePending, 0);
                RenderFrame();
                UpdateVisibility();
            });
        }
    }

    private void RenderFrame()
    {
        if (_disposed)
        {
            return; // A queued render can still run during shutdown.
        }

        var sessions = _store.Snapshot();
        var now = DateTimeOffset.Now;
        var frame = _composer.Compose(sessions, now);
        _selectedSessionId = frame.SelectedSessionId;
        _viewModel.ShowSessionList = _settings.Panel.ShowSessionList;
        _viewModel.Apply(frame, sessions, now);
        _tray?.SetStatus(sessions.Count == 0 ? SessionStatus.Idle : sessions.Max(s => s.Status));

        // Alerts are only for full-screen moments: everywhere else the panel already tells the story.
        var alerts = _alerts.Detect(sessions, now);
        PlaySoundFor(alerts, sessions, now);
        if (alerts.Count == 0 || _allInSight
            || _settings.Panel.Fullscreen is not (FullscreenBehavior.EdgeAndToast or FullscreenBehavior.ToastOnly))
        {
            return;
        }

        foreach (var monitor in _targets.Where(m => _fullscreen.GetValueOrDefault(m.Handle) == FullscreenKind.Borderless))
        {
            foreach (var alert in alerts)
            {
                _overlays.ShowAlert(alert with { Text = _viewModel.Cased(alert.Text) }, monitor);
            }
        }
    }

    // A sound reaches the user wherever they are looking, full screen included, so it is not tied to the
    // overlays below: only to the session's app being out of sight.
    private void PlaySoundFor(IReadOnlyList<PanelAlert> alerts, IReadOnlyList<Session> sessions, DateTimeOffset now)
    {
        if (alerts.Count == 0)
        {
            return;
        }

        var threshold = _settings.Panel.VisibleThreshold;
        var cue = _soundCues.Pick(
            alerts,
            id => sessions.FirstOrDefault(s => s.Id == id) is { } session
                && !SessionVisibility.IsInSight(session, _hostWindows, threshold),
            _settings.Panel,
            now);

        if (cue is { } chosen)
        {
            Play(chosen);
        }
    }

    private void Play(SoundCue cue)
    {
        var style = _settings.Panel.SoundStyle == SoundStyle.Auto
            ? ThemeCatalog.Resolve(_settings.Panel.Theme).Sound
            : _settings.Panel.SoundStyle;
        if (!_sound.Play(ToneSynth.Render(style, cue)) && !_soundFailureLogged)
        {
            _soundFailureLogged = true; // Once: a PC without an audio device would fill the log.
            _log.Info("Windows could not play the notification sound.");
        }
    }

    // Turning a sound on plays it, so the user hears what they just asked for.
    private void ToggleSound(SoundCue cue)
    {
        var panel = _settings.Panel;
        panel = cue == SoundCue.Done
            ? panel with { SoundOnDone = !panel.SoundOnDone }
            : panel with { SoundOnAttention = !panel.SoundOnAttention };
        UpdatePanel(panel);

        if (cue == SoundCue.Done ? panel.SoundOnDone : panel.SoundOnAttention)
        {
            Play(cue);
        }
    }

    private void UpdateVisibility()
    {
        if (_disposed)
        {
            return; // Windows are closed; never resurrect them.
        }

        try
        {
            var claude = _probe.Check();
            _claudeWindow = claude.Handle;
            _hostWindows = claude.ToHostWindows();
            var threshold = _settings.Panel.VisibleThreshold;
            _allInSight = !SessionVisibility.AnyOutOfSight(_store.Snapshot(), _hostWindows, threshold);

            // Opening the session's app (Claude or VS Code) is how the user "sees" it finished: stop blinking.
            if (_store.AcknowledgeCompleted(s => SessionVisibility.IsInSight(s, _hostWindows, threshold)))
            {
                RenderFrame();
            }

            if (_allInSight)
            {
                _overlays.DismissAlerts();
            }

            var panel = _settings.Panel;
            _panels.Settings = panel;
            _monitors = [.. Monitors.All().Select(m => m with { TaskbarItems = _taskbarButtons?.On(m.Taskbar) })];
            _targets = PanelManager.Targets(_monitors, panel);
            _fullscreen = _targets.ToDictionary(m => m.Handle, FullscreenDetector.Detect);

            var shouldShow = panel.TopActivityBar || (!_allInSight && (_viewModel.HasActivity || !panel.HideWhenIdle));
            var preview = _menuOpen || DateTimeOffset.Now < _previewUntil;
            _panels.Sync(_targets, _fullscreen, shouldShow, preview);

            var glow = panel.Fullscreen == FullscreenBehavior.EdgeAndToast
                && !_allInSight
                && (_viewModel.IsBlinking || _viewModel.NeedsAttention);
            _overlays.SyncEdges(
                glow ? [.. _targets.Where(m => _fullscreen[m.Handle] == FullscreenKind.Borderless)] : [],
                _viewModel.NeedsAttention);

            ShowRightClickHintOnce();
        }
#pragma warning disable CA1031 // The loop must keep running whatever a Win32 call does.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            _log.Error("Visibility update failed.", ex);
        }
    }

    // The menu lives behind a right click on the panel; say so the first time the panel appears.
    private void ShowRightClickHintOnce()
    {
        if (_settings.RightClickHintShown || _hintTimer is not null || _viewModel.IsEditing || !_panels.AnyVisible)
        {
            return;
        }

        _viewModel.Hint = _strings.RightClickHint;
        _hintTimer = new DispatcherTimer(HintDuration, DispatcherPriority.Background, (_, _) =>
        {
            _hintTimer?.Stop();
            _viewModel.Hint = null;
        }, Dispatcher.CurrentDispatcher);

        _settings = _settings with { RightClickHintShown = true };
        _settingsStore.Save(_settings);
    }

    // Reads transcripts off the UI thread; skips a tick while the previous check is still running.
    private void CheckQuietSessions()
    {
        if (_eventPump is not { } pump || Interlocked.Exchange(ref _quietCheckRunning, 1) == 1)
        {
            return;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                await pump.CheckQuietSessionsAsync(QuietAfter, _stop.Token).ConfigureAwait(false);
            }
            finally
            {
                Volatile.Write(ref _quietCheckRunning, 0);
            }
        });
    }

    private void Prune()
    {
        if (_store.PruneStale(TimeSpan.FromHours(_settings.Panel.StaleSessionHours)))
        {
            RenderFrame();
        }
    }

    // While the menu is open, and a few seconds after, the panel is shown as a live preview:
    // picking a theme or a position is visible immediately, even with Claude on screen.
    private void OnMenuVisibilityChanged(bool open)
    {
        _menuOpen = open;
        if (!open)
        {
            _previewUntil = DateTimeOffset.Now + PreviewLinger;
        }

        UpdateVisibility();
    }

    private void ApplyTheme(string? id)
    {
        ThemeCatalog.Apply(Application.Current.Resources, id);
        var theme = ThemeCatalog.Resolve(id);
        _viewModel.Uppercase = theme.Uppercase;
        _viewModel.IsLineMap = theme.Id == "LineMap";
    }

    // Users' JSON themes: read at startup and again each time the menu opens, so a new or edited
    // file shows up without a restart. Bad files are skipped and explained in the log.
    private void LoadCustomThemes()
    {
        var before = ThemeCatalog.Resolve(_settings.Panel.Theme).Custom;
        var problems = ThemeCatalog.LoadCustom(ThemeCatalog.CustomFolder);

        // Each problem once, not on every menu open; a file fixed and broken again is reported again.
        foreach (var problem in problems.Where(p => !_reportedThemeProblems.Contains(p)))
        {
            _log.Info(problem);
        }

        _reportedThemeProblems = [.. problems];

        // The theme in use is a custom one that changed on disk: show the new version right away.
        // Compared by content: the record holds a dictionary, which compares by reference.
        var after = ThemeCatalog.Resolve(_settings.Panel.Theme).Custom;
        if ((before is not null || after is not null)
            && System.Text.Json.JsonSerializer.Serialize(before) != System.Text.Json.JsonSerializer.Serialize(after))
        {
            ApplyTheme(_settings.Panel.Theme);
        }
    }

    private void OpenThemesFolder()
    {
        try
        {
            var folder = Directory.CreateDirectory(ThemeCatalog.CustomFolder);
            if (!folder.EnumerateFiles("*.json").Any())
            {
                File.WriteAllText(Path.Combine(folder.FullName, ExampleTheme.FileName), ExampleTheme.Json);
                LoadCustomThemes();
            }

            Process.Start(new ProcessStartInfo(folder.FullName) { UseShellExecute = true })?.Dispose();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            _log.Error("Could not open the themes folder.", ex);
        }
    }

    // A click on the feed (or an alert, or the tray): a finished session first, else the one in the feed.
    private void OpenClaude() => OpenSession(ClickTarget.ForFeed(_store.Snapshot(), _selectedSessionId));

    // A click on a tab: that session goes to the feed and its app comes to the front.
    private void OpenTab(string sessionId)
    {
        var session = ClickTarget.ForTab(_store.Snapshot(), sessionId);
        if (session is not null)
        {
            _composer.Rotation.Show(session.Id, DateTimeOffset.Now);
            RenderFrame();
        }

        OpenSession(session);
    }

    // Brings up the app the session runs in: its project's VS Code window, Claude otherwise. Only the window:
    // reopening a conversation could start a second copy of one already running elsewhere.
    private void OpenSession(Session? session)
    {
        if (session is { Host: SessionHost.VsCode }
            && SessionVisibility.EditorFor(session, _hostWindows.Editors) is { } editor)
        {
            WindowActions.Activate(editor.Handle);
        }
        else if (_claudeWindow != 0)
        {
            WindowActions.Activate(_claudeWindow);
        }
        else if (!WindowActions.LaunchClaude())
        {
            _log.Info("Claude window not found and claude:// protocol not available.");
        }
    }

    private void BeginEditing()
    {
        _editOrigin = _settings.Panel;
        _viewModel.Hint = null;
        _viewModel.IsEditing = true;
        UpdateVisibility();
    }

    /// <summary>Enter keeps the new position; Escape restores where the panel was.</summary>
    private void FinishEditing(bool save)
    {
        if (!_viewModel.IsEditing)
        {
            return;
        }

        if (!save && _editOrigin is { } origin)
        {
            _settings = _settings with { Panel = origin };
        }

        _editOrigin = null;
        _viewModel.IsEditing = false;
        _settingsStore.Save(_settings);
        UpdateVisibility();
    }

    private void EnsureAutoStart()
    {
        try
        {
            if (_settings.StartWithWindows && !_autoStart.IsEnabled)
            {
                _autoStart.Enable(); // Also repairs the path after the app moved.
            }
            else if (!_settings.StartWithWindows && _autoStart.IsEnabled)
            {
                _autoStart.Disable();
            }
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        {
            _log.Error("Could not update auto-start.", ex);
        }
    }

    private void EnsureClaudeCodeHooks()
    {
        try
        {
            if (_settings.ClaudeCodeHooksEnabled is null)
            {
                var answer = MessageBox.Show(_strings.ConnectPrompt, _strings.Tooltip, MessageBoxButton.YesNo, MessageBoxImage.Question);
                _settings = _settings with { ClaudeCodeHooksEnabled = answer == MessageBoxResult.Yes };
                _settingsStore.Save(_settings);
            }

            if (_settings.ClaudeCodeHooksEnabled == true && !_hooks.IsInstalled())
            {
                _hooks.Install(); // First connect, or the app moved and hooks point to the old path.
                _log.Info("Claude Code hooks installed.");
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            _log.Error("Could not update Claude Code hooks.", ex);
            _tray?.ShowError(string.Format(_culture, _strings.ConnectFailed, ex.Message));
        }
    }

    private TrayState TrayState()
    {
        LoadCustomThemes(); // The menu is about to list themes: pick up files added or edited since.
        return new(
        _viewModel.IsEditing,
        _settings.Panel,
        _monitors,
        _settings.StartWithWindows,
        _settings.ClaudeCodeHooksEnabled == true,
        _settings.Language);
    }

    private TrayCommands CreateTrayCommands() => new(
        ToggleEditing: () =>
        {
            if (_viewModel.IsEditing)
            {
                FinishEditing(save: true);
            }
            else
            {
                BeginEditing();
            }
        },
        SelectMonitor: (mode, device) => UpdatePanel(_settings.Panel with { MonitorMode = mode, MonitorDeviceName = device }),
        SelectPlacement: placement => UpdatePanel(_settings.Panel with { Placement = placement }),
        SelectFullscreen: behavior => UpdatePanel(_settings.Panel with { Fullscreen = behavior }),
        SelectTheme: theme =>
        {
            UpdatePanel(_settings.Panel with { Theme = theme });
            ApplyTheme(theme); // Live: every color is a DynamicResource.
        },
        ToggleSessionList: () =>
        {
            UpdatePanel(_settings.Panel with { ShowSessionList = !_settings.Panel.ShowSessionList });
            RenderFrame();
            UpdateVisibility();
        },
        ToggleTopActivityBar: () => UpdatePanel(_settings.Panel with { TopActivityBar = !_settings.Panel.TopActivityBar }),
        SelectOpacity: opacity => UpdatePanel(_settings.Panel with { BackgroundOpacity = opacity }),
        ToggleSoundOnDone: () => ToggleSound(SoundCue.Done),
        ToggleSoundOnAttention: () => ToggleSound(SoundCue.Attention),
        SelectSoundStyle: style =>
        {
            UpdatePanel(_settings.Panel with { SoundStyle = style });
            Play(SoundCue.Done); // Hear the voice just picked.
        },
        SelectLanguage: SelectLanguage,
        ToggleHideWhenIdle: () => UpdatePanel(_settings.Panel with { HideWhenIdle = !_settings.Panel.HideWhenIdle }),
        ToggleStartWithWindows: () =>
        {
            _settings = _settings with { StartWithWindows = !_settings.StartWithWindows };
            _settingsStore.Save(_settings);
            EnsureAutoStart();
        },
        ToggleClaudeCode: ToggleClaudeCode,
        OpenSettingsFolder: () => Process.Start(new ProcessStartInfo(Path.GetDirectoryName(_settingsStore.FilePath)!) { UseShellExecute = true })?.Dispose(),
        OpenThemesFolder: OpenThemesFolder,
        OpenClaude: OpenClaude,
        Exit: ShutdownAfterMenu);

    private void UpdatePanel(PanelSettings panel)
    {
        _settings = _settings with { Panel = panel.Normalize() };
        if (_viewModel.IsEditing)
        {
            _editOrigin = _settings.Panel; // Switching mode while editing is itself a choice; Escape should not undo it.
        }

        _settingsStore.Save(_settings);
        UpdateVisibility();
    }

    // Text is baked into several components at startup, so a language change restarts the app (about a second).
    private void SelectLanguage(string language)
    {
        if (language == _settings.Language)
        {
            return;
        }

        _settings = _settings with { Language = language };
        _settingsStore.Save(_settings);

        if (Environment.ProcessPath is { } exe)
        {
            Process.Start(new ProcessStartInfo(exe, App.RestartArgument) { UseShellExecute = false })?.Dispose();
        }

        ShutdownAfterMenu();
    }

    // Called from a menu click: let the menu finish closing first, then exit. Shutting down inside the
    // click would dispose the menu while WinForms is still using it.
    private static void ShutdownAfterMenu() =>
        Application.Current.Dispatcher.BeginInvoke(() => Application.Current.Shutdown(), DispatcherPriority.Background);

    private void ToggleClaudeCode()
    {
        var connect = _settings.ClaudeCodeHooksEnabled != true;
        _settings = _settings with { ClaudeCodeHooksEnabled = connect };
        _settingsStore.Save(_settings);
        try
        {
            if (connect)
            {
                _hooks.Install();
            }
            else
            {
                _hooks.Uninstall();
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            _log.Error("Could not update Claude Code hooks.", ex);
            _tray?.ShowError(string.Format(_culture, _strings.ConnectFailed, ex.Message));
        }
    }
}
