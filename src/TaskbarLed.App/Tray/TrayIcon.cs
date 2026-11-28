using System.Globalization;
using System.Windows.Forms;
using TaskbarLed.App.Infrastructure;
using TaskbarLed.Core.Sessions;
using TaskbarLed.Core.Settings;
using TaskbarLed.Platform.Windows.Windowing;

namespace TaskbarLed.App.Tray;

/// <summary>Current values the tray menu displays (read each time the menu opens).</summary>
public sealed record TrayState(
    bool IsEditing,
    PanelSettings Panel,
    IReadOnlyList<MonitorInfo> Monitors,
    bool StartWithWindows,
    bool ClaudeCodeConnected,
    string Language);

/// <summary>What the tray menu can ask the app to do.</summary>
public sealed record TrayCommands(
    Action ToggleEditing,
    Action<MonitorMode, string?> SelectMonitor,
    Action<PanelPlacement> SelectPlacement,
    Action<FullscreenBehavior> SelectFullscreen,
    Action<string> SelectTheme,
    Action<double> SelectOpacity,
    Action ToggleSessionList,
    Action ToggleTopActivityBar,
    Action ToggleSoundOnDone,
    Action ToggleSoundOnAttention,
    Action<SoundStyle> SelectSoundStyle,
    Action<string> SelectLanguage,
    Action ToggleHideWhenIdle,
    Action ToggleStartWithWindows,
    Action ToggleClaudeCode,
    Action OpenSettingsFolder,
    Action OpenThemesFolder,
    Action OpenClaude,
    Action Exit);

/// <summary>Notification-area icon and menu. The icon color mirrors the most urgent session.</summary>
public sealed class TrayIcon : IDisposable
{
    // Tag for menu items that run an action inside a "keep open" submenu.
    private static readonly object ClosesMenu = new();

    private readonly NotifyIcon _icon;
    private readonly ContextMenuStrip _menu = new();
    private readonly Dictionary<SessionStatus, System.Drawing.Icon> _icons = [];
    private readonly UiStrings _strings;
    private readonly Func<TrayState> _state;
    private readonly TrayCommands _commands;
    private SessionStatus _status = (SessionStatus)(-1);
    private bool _keepOpen;

    public TrayIcon(UiStrings strings, Func<TrayState> state, TrayCommands commands)
    {
        _strings = strings;
        _state = state;
        _commands = commands;

        _menu.Opening += (_, _) => Rebuild();
        _menu.Closing += (_, e) =>
        {
            if (_keepOpen && e.CloseReason == ToolStripDropDownCloseReason.ItemClicked)
            {
                e.Cancel = true;
            }
        };
        _menu.Opened += (_, _) => MenuVisibilityChanged?.Invoke(this, true);
        _menu.Closed += (_, _) => MenuVisibilityChanged?.Invoke(this, false);
        _icon = new NotifyIcon { Text = strings.Tooltip, ContextMenuStrip = _menu, Visible = true };
        _icon.MouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left)
            {
                _commands.OpenClaude();
            }
        };
        SetStatus(SessionStatus.Idle);
        Rebuild(); // A first menu so the tray is usable before any right click.
    }

    public void SetStatus(SessionStatus status)
    {
        if (status == _status)
        {
            return;
        }

        _status = status;
        if (!_icons.TryGetValue(status, out var icon))
        {
            icon = TrayIconFactory.Create(status);
            _icons[status] = icon;
        }

        _icon.Icon = icon;
    }

    /// <summary>Opens the same menu at the mouse position (right click on the panel).</summary>
    public void ShowMenuAtCursor() => _menu.Show(Cursor.Position);

    /// <summary>The menu opened (true) or closed (false). While it is open the panel stays on screen as a live preview.</summary>
    public event EventHandler<bool>? MenuVisibilityChanged;

    public void ShowError(string message) =>
        _icon.ShowBalloonTip(5000, _strings.Tooltip, message, ToolTipIcon.Warning);

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
        _menu.Dispose();
        foreach (var icon in _icons.Values)
        {
            icon.Dispose();
        }
    }

    private void Rebuild()
    {
        var state = _state();
        _menu.Items.Clear();

        _menu.Items.Add(new ToolStripMenuItem(state.IsEditing ? _strings.FinishEditing : _strings.EditPosition, null, (_, _) => _commands.ToggleEditing()));

        var placementMenu = new ToolStripMenuItem(_strings.Placement);
        foreach (var (placement, label) in new[]
                 {
                     (PanelPlacement.Taskbar, _strings.PlacementTaskbar),
                     (PanelPlacement.AboveTaskbar, _strings.PlacementAbove),
                     (PanelPlacement.Free, _strings.PlacementFree),
                 })
        {
            placementMenu.DropDownItems.Add(Check(label, state.Panel.Placement == placement, () => _commands.SelectPlacement(placement)));
        }

        KeepOpenOnPick(placementMenu);
        _menu.Items.Add(placementMenu);

        var fullscreenMenu = new ToolStripMenuItem(_strings.Fullscreen);
        foreach (var (behavior, label) in new[]
                 {
                     (FullscreenBehavior.EdgeAndToast, _strings.FullscreenEdgeAndToast),
                     (FullscreenBehavior.ToastOnly, _strings.FullscreenToastOnly),
                     (FullscreenBehavior.MiniPanel, _strings.FullscreenMiniPanel),
                     (FullscreenBehavior.Hide, _strings.FullscreenHide),
                 })
        {
            fullscreenMenu.DropDownItems.Add(Check(label, state.Panel.Fullscreen == behavior, () => _commands.SelectFullscreen(behavior)));
        }

        KeepOpenOnPick(fullscreenMenu);
        _menu.Items.Add(fullscreenMenu);

        var themeMenu = new ToolStripMenuItem(_strings.Theme);
        var currentTheme = Themes.ThemeCatalog.Resolve(state.Panel.Theme).Id;
        foreach (var theme in Themes.ThemeCatalog.All)
        {
            AddTheme(themeMenu, theme, currentTheme);
        }

        // The user's own JSON themes, then the way to the folder where they live.
        themeMenu.DropDownItems.Add(new ToolStripSeparator());
        foreach (var theme in Themes.ThemeCatalog.Custom)
        {
            AddTheme(themeMenu, theme, currentTheme);
        }

        themeMenu.DropDownItems.Add(new ToolStripMenuItem(_strings.OpenThemesFolder, null, (_, _) => _commands.OpenThemesFolder()) { Tag = ClosesMenu });

        KeepOpenOnPick(themeMenu);
        _menu.Items.Add(themeMenu);

        var opacityMenu = new ToolStripMenuItem(_strings.BackgroundOpacity);
        foreach (var opacity in new[] { 1.0, 0.85, 0.70, 0.55, 0.35 })
        {
            opacityMenu.DropDownItems.Add(Check(opacity.ToString("P0", CultureInfo.CurrentCulture),
                state.Panel.BackgroundOpacity == opacity, () => _commands.SelectOpacity(opacity)));
        }

        KeepOpenOnPick(opacityMenu);
        _menu.Items.Add(opacityMenu);
        _menu.Items.Add(Check(_strings.SessionList, state.Panel.ShowSessionList, _commands.ToggleSessionList));
        _menu.Items.Add(Check(_strings.TopActivityBar, state.Panel.TopActivityBar, _commands.ToggleTopActivityBar));
        _menu.Items.Add(SoundMenu(state.Panel));

        // Language names are written in their own language so anyone can find theirs.
        var languageMenu = new ToolStripMenuItem(_strings.Language);
        foreach (var (code, label) in new[] { ("auto", _strings.LanguageAuto), ("pt", "Português"), ("en", "English") })
        {
            languageMenu.DropDownItems.Add(Check(label, state.Language == code, () => _commands.SelectLanguage(code)));
        }

        _menu.Items.Add(languageMenu);

        var monitorMenu = new ToolStripMenuItem(_strings.Monitor);
        monitorMenu.DropDownItems.Add(Check(_strings.PrimaryMonitor, state.Panel.MonitorMode == MonitorMode.Primary, () => _commands.SelectMonitor(MonitorMode.Primary, null)));
        monitorMenu.DropDownItems.Add(Check(_strings.AllMonitors, state.Panel.MonitorMode == MonitorMode.All, () => _commands.SelectMonitor(MonitorMode.All, null)));
        if (state.Monitors.Count > 1)
        {
            monitorMenu.DropDownItems.Add(new ToolStripSeparator());
            var index = 1;
            foreach (var monitor in state.Monitors)
            {
                var label = string.Format(CultureInfo.CurrentCulture, _strings.MonitorItem, index++, monitor.Bounds.Width, monitor.Bounds.Height);
                var selected = state.Panel.MonitorMode == MonitorMode.Specific && state.Panel.MonitorDeviceName == monitor.DeviceName;
                var device = monitor.DeviceName;
                monitorMenu.DropDownItems.Add(Check(label, selected, () => _commands.SelectMonitor(MonitorMode.Specific, device)));
            }
        }

        KeepOpenOnPick(monitorMenu);
        _menu.Items.Add(monitorMenu);
        _menu.Items.Add(Check(_strings.HideWhenIdle, state.Panel.HideWhenIdle, _commands.ToggleHideWhenIdle));
        _menu.Items.Add(Check(_strings.StartWithWindows, state.StartWithWindows, _commands.ToggleStartWithWindows));
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add(new ToolStripMenuItem(state.ClaudeCodeConnected ? _strings.DisconnectClaudeCode : _strings.ConnectClaudeCode, null, (_, _) => _commands.ToggleClaudeCode()));
        _menu.Items.Add(new ToolStripMenuItem(_strings.OpenSettingsFolder, null, (_, _) => _commands.OpenSettingsFolder()));
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add(new ToolStripMenuItem(_strings.Exit, null, (_, _) => _commands.Exit()));
    }

    // Two independent switches, then one voice out of several. Every click plays what was picked, and the
    // menu stays open so the voices can be compared.
    private ToolStripMenuItem SoundMenu(PanelSettings panel)
    {
        var menu = new ToolStripMenuItem(_strings.Sound);
        menu.DropDownItems.Add(Switch(_strings.SoundOnDone, panel.SoundOnDone, _commands.ToggleSoundOnDone));
        menu.DropDownItems.Add(Switch(_strings.SoundOnAttention, panel.SoundOnAttention, _commands.ToggleSoundOnAttention));
        menu.DropDownItems.Add(new ToolStripSeparator());

        var styles = new List<ToolStripMenuItem>();
        foreach (var (style, label) in new[]
                 {
                     (SoundStyle.Auto, _strings.SoundMatchTheme),
                     (SoundStyle.Chime, _strings.SoundChime),
                     (SoundStyle.Retro, _strings.SoundRetro),
                     (SoundStyle.Bell, _strings.SoundBell),
                 })
        {
            var item = new ToolStripMenuItem(label) { Checked = panel.SoundStyle == style };
            item.Click += (_, _) =>
            {
                styles.ForEach(other => other.Checked = ReferenceEquals(other, item));
                _commands.SelectSoundStyle(style);
            };
            styles.Add(item);
            menu.DropDownItems.Add(item);
        }

        KeepOpenOnPick(menu, exclusive: false);
        return menu;

        static ToolStripMenuItem Switch(string text, bool isOn, Action toggle)
        {
            var item = new ToolStripMenuItem(text) { Checked = isOn };
            item.Click += (_, _) =>
            {
                item.Checked = !item.Checked;
                toggle();
            };
            return item;
        }
    }

    private static ToolStripMenuItem Check(string text, bool isChecked, Action onClick) =>
        new(text, null, (_, _) => onClick()) { Checked = isChecked };

    private void AddTheme(ToolStripMenuItem menu, Themes.ThemeInfo theme, string currentId)
    {
        var id = theme.Id;
        var selected = string.Equals(currentId, id, StringComparison.Ordinal);
        menu.DropDownItems.Add(Check(theme.DisplayName, selected, () => _commands.SelectTheme(id)));
    }

    /// <summary>
    /// Picking an option in <paramref name="submenu"/> applies it but keeps the menu open, so the user can
    /// try theme after theme (or position after position) and watch the panel change, without reopening the menu.
    /// </summary>
    /// <param name="exclusive">The submenu is one choice out of many: move the checkmark to the clicked item.</param>
    private void KeepOpenOnPick(ToolStripMenuItem submenu, bool exclusive = true)
    {
        submenu.DropDown.ItemClicked += (_, e) =>
        {
            if (ReferenceEquals(e.ClickedItem?.Tag, ClosesMenu))
            {
                return; // An action, not an option: let the menu close and keep the checkmarks as they are.
            }

            _keepOpen = true;
            foreach (var item in exclusive ? submenu.DropDownItems.OfType<ToolStripMenuItem>() : [])
            {
                item.Checked = ReferenceEquals(item, e.ClickedItem);
            }

            // The menus close synchronously right after this event; reset once that is over.
            _menu.BeginInvoke(() => _keepOpen = false);
        };

        submenu.DropDown.Closing += (_, e) =>
        {
            if (_keepOpen && e.CloseReason == ToolStripDropDownCloseReason.ItemClicked)
            {
                e.Cancel = true;
            }
        };
    }
}
