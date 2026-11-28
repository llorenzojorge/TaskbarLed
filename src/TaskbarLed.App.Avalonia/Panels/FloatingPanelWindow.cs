using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Threading;
using TaskbarLed.App.Avalonia.Presentation;
using TaskbarLed.Core.Presentation;
using TaskbarLed.Core.Visibility;
using TaskbarLed.Platform.Mac;
using CorePixelRect = TaskbarLed.Core.Visibility.PixelRect;

namespace TaskbarLed.App.Avalonia.Panels;

/// <summary>
/// The small borderless panel, centered horizontally just above the Dock (or the taskbar on Windows).
/// Shows live sessions independently of the foreground application.
/// </summary>
internal sealed class FloatingPanelWindow : Window
{
    /// <summary>Panel width in DIP/points.</summary>
    public const double PanelWidth = 420;

    /// <summary>Space between the panel and the Dock, in DIP/points.</summary>
    public const double DockGap = 8;

    public const double PanelHeight = PanelLayout.FloatingHeightDip + 29;

    private readonly PanelViewModel _viewModel;
    private readonly Func<PanelFrame> _frames;
    private readonly DispatcherTimer _frameTimer;
    private readonly Action<string?>? _acknowledge;
    private readonly Action? _skip;
    private PanelFrame? _frame;
    private const double MinPanelWidth = 300;
    private const double EditorHeight = 76;
    private const double MinPanelOpacity = 0.70;
    private double _panelWidth = PanelWidth;
    private double _panelHeight = PanelHeight;
    private Grid? _preview;
    private bool _automaticEdit;
    private nint _nativeGlass;
    private double _glassOpacity;
    private bool _crystalGlass;
    private readonly PanelView _panel;
    private readonly MacUiStrings _strings;
    private PixelPoint? _beforeEdit;
    private (PixelPoint Pointer, PixelPoint Window, double Width, double Height, bool ResizeWidth, bool ResizeHeight)? _drag;
    private PanelPreferences _preferences;
    private Action<PanelPreferences>? _savePreferences;

    public bool KeepVisible { get; set; }

    public FloatingPanelWindow(PanelViewModel viewModel, Func<PanelFrame> frames,
        Action<string?>? acknowledge = null, Action? skip = null, bool keepVisible = false, bool portuguese = false)
    {
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        _frames = frames ?? throw new ArgumentNullException(nameof(frames));

        _acknowledge = acknowledge;
        _skip = skip;
        KeepVisible = keepVisible;
        _strings = MacUiStrings.For(portuguese);
        _preferences = new PanelPreferences();

        Title = App.DisplayName;
        WindowDecorations = global::Avalonia.Controls.WindowDecorations.None;
        CanResize = false;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        Background = Brushes.Transparent;
        TransparencyLevelHint = [WindowTransparencyLevel.Transparent];
        WindowStartupLocation = WindowStartupLocation.Manual;
        Width = PanelWidth;
        Height = PanelHeight;
        _panel = new PanelView { DataContext = viewModel };
        Content = _panel;
        _panel.FindControl<Button>("IslandToggle")!.Click += (_, _) =>
        {
            _viewModel.IslandExpanded = !_viewModel.IslandExpanded;
            RestorePlacement();
        };

        _frameTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(250), DispatcherPriority.Background, (_, _) => Render());
        ApplyFrame();
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        RestorePlacement();

        if (OperatingSystem.IsMacOS())
        {
            // The default handle is the NSView; the window behavior lives on its NSWindow.
            if (TryGetPlatformHandle() is IMacOSTopLevelPlatformHandle mac)
            {
                MacFloatingWindow.JoinAllSpaces(mac.NSWindow);
            }
        }

        UpdateNativeGlass();
        _frameTimer.Start();
    }

    protected override void OnClosed(EventArgs e)
    {
        _nativeGlass = 0;
        _frameTimer.Stop();
        base.OnClosed(e);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        base.OnPointerPressed(e);
        var point = e.GetCurrentPoint(this);
        if (_beforeEdit.HasValue)
        {
            if (point.Properties.IsLeftButtonPressed && point.Position.Y < _preview!.Bounds.Height)
            {
                var resizeWidth = !_automaticEdit && point.Position.X >= Width - 15;
                var resizeHeight = !_automaticEdit && point.Position.Y >= _preview.Bounds.Height - 15;
                _drag = (this.PointToScreen(point.Position), Position, Width, _preview.Bounds.Height, resizeWidth, resizeHeight);
                e.Pointer.Capture(this);
                e.Handled = true;
            }

            return;
        }

        if (point.Properties.IsLeftButtonPressed)
        {
            var context = (e.Source as Control)?.DataContext;
            if (context is SessionDisplayRowViewModel { SessionId: { } sessionId })
            {
                _acknowledge?.Invoke(sessionId);
            }
            else if (_frame is { SessionRows.Count: 0 })
            {
                _acknowledge?.Invoke((context as ChannelViewModel)?.SessionId ?? _frame.SelectedSessionId);
            }

            Render();
        }
    }

    /// <summary>Rectangle for the panel on a screen's work area. Shared with the screenshot test.</summary>
    internal static CorePixelRect Layout(global::Avalonia.PixelRect workArea, double scaling) =>
        DockLayout(workArea, scaling, PanelHeight);

    private static CorePixelRect DockLayout(global::Avalonia.PixelRect workArea, double scaling, double height, double width = PanelWidth)
    {
        var rect = PanelLayout.AboveDock(
            new CorePixelRect(workArea.X, workArea.Y, workArea.Right, workArea.Bottom), scaling, width, DockGap);
        return rect with { Top = Math.Max(workArea.Y, rect.Bottom - (int)Math.Round(height * scaling)) };
    }

    public void ConfigurePlacement(PanelPreferences preferences, Action<PanelPreferences> save)
    {
        _preferences = preferences;
        _viewModel.PanelOpacity = NormalizeOpacity(preferences.PanelOpacity);
        _savePreferences = save;
        ApplyFrame();
    }

    public void SetTheme(string id) => Persist(_preferences with { Theme = id });

    private nint? _previousWindowLevel;

    public bool IsDynamicIsland => _preferences.DynamicIsland;

    public void SetDynamicIsland(bool enabled)
    {
        if (_beforeEdit.HasValue)
        {
            EndEdit(save: false);
        }

        Persist(_preferences with { DynamicIsland = enabled });
        _viewModel.IslandExpanded = false;
        RestorePlacement();
    }

    public void ResetPosition()
    {
        if (_beforeEdit.HasValue)
        {
            EndEdit(save: false);
        }

        Persist(_preferences with { X = null, Y = null, Width = PanelWidth, Height = null, AutomaticSize = true });
        ApplyFrame();
        RestorePlacement();
    }

    public void BeginEdit()
    {
        if (_beforeEdit.HasValue)
        {
            return;
        }

        if (!IsVisible)
        {
            Show();
            RestorePlacement();
        }

        _beforeEdit = Position;
        _automaticEdit = _preferences.UsesAutomaticSize();
        var save = EditButton(_strings.Save, primary: true);
        var cancel = EditButton(_strings.Cancel, primary: false);
        save.Click += (_, _) => EndEdit(save: true);
        cancel.Click += (_, _) => EndEdit(save: false);

        var right = ResizeGrip(StandardCursorType.SizeWestEast, HorizontalAlignment.Right, VerticalAlignment.Stretch);
        var bottom = ResizeGrip(StandardCursorType.SizeNorthSouth, HorizontalAlignment.Stretch, VerticalAlignment.Bottom);
        var corner = ResizeGrip(StandardCursorType.BottomRightCorner, HorizontalAlignment.Right, VerticalAlignment.Bottom);
        var grips = new Grid { Children = { right, bottom, corner }, IsVisible = !_automaticEdit };
        var sizeMode = new ComboBox
        {
            ItemsSource = new[] { _strings.Automatic, _strings.Manual },
            SelectedIndex = _automaticEdit ? 0 : 1,
            Background = new SolidColorBrush(Color.Parse("#E8EDF0")),
            Foreground = new SolidColorBrush(Color.Parse("#202C35")),
            [global::Avalonia.Automation.AutomationProperties.NameProperty] = _strings.SizeMode,
        };
        sizeMode.SelectionChanged += (_, _) =>
        {
            _automaticEdit = sizeMode.SelectedIndex == 0;
            grips.IsVisible = !_automaticEdit;
            ApplyFrame();
        };

        var opacityLabel = new TextBlock { VerticalAlignment = VerticalAlignment.Center };
        var opacity = new Slider
        {
            Minimum = MinPanelOpacity * 100,
            Maximum = 100,
            Value = _viewModel.PanelOpacity * 100,
            TickFrequency = 1,
            IsSnapToTickEnabled = true,
            MinWidth = 108,
            [global::Avalonia.Automation.AutomationProperties.NameProperty] = _strings.Opacity,
        };
        var opacityRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            HorizontalAlignment = HorizontalAlignment.Center,
            Children = { opacityLabel, opacity },
        };
        opacityLabel.Bind(TextBlock.ForegroundProperty,
            new global::Avalonia.Markup.Xaml.MarkupExtensions.DynamicResourceExtension("Led.ForegroundBrush"));
        void PreviewOpacity()
        {
            _viewModel.PanelOpacity = NormalizeOpacity(opacity.Value / 100);
            opacityLabel.Text = $"{(_strings.Opacity)} {opacity.Value:F0}%";
            UpdateNativeGlass();
        }
        opacity.ValueChanged += (_, _) => PreviewOpacity();
        PreviewOpacity();

        Content = null;
        _preview = new Grid { Cursor = new Cursor(StandardCursorType.SizeAll), Children = { _panel, grips } };
        var footer = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Children = { sizeMode, save, cancel },
        };
        var controls = new Border { IsHitTestVisible = false };
        controls.Bind(Border.BackgroundProperty, new global::Avalonia.Markup.Xaml.MarkupExtensions.DynamicResourceExtension("Led.BackgroundBrush"));
        controls.ZIndex = -1;
        Grid.SetRow(controls, 1);
        Grid.SetRowSpan(controls, 2);
        var editor = new Grid { RowDefinitions = new RowDefinitions("*,40,36"), Children = { _preview, footer, opacityRow, controls } };
        Grid.SetRow(footer, 1);
        Grid.SetRow(opacityRow, 2);
        var border = new Border { Child = editor, BorderThickness = new Thickness(1) };
        border.Bind(Border.BorderBrushProperty, new global::Avalonia.Markup.Xaml.MarkupExtensions.DynamicResourceExtension("Led.EditBrush"));
        Content = border;
        RestorePlacement();
        Activate();
        save.Focus();
    }

    private static Border ResizeGrip(StandardCursorType cursor, HorizontalAlignment horizontal, VerticalAlignment vertical)
    {
        var grip = new Border
        {
            HorizontalAlignment = horizontal,
            VerticalAlignment = vertical,
            Width = horizontal == HorizontalAlignment.Right ? 15 : double.NaN,
            Height = vertical == VerticalAlignment.Bottom ? 15 : double.NaN,
            Background = Brushes.Transparent,
            Cursor = new Cursor(cursor),
            Child = new Border
            {
                Width = horizontal == HorizontalAlignment.Right ? 3 : 24,
                Height = vertical == VerticalAlignment.Bottom ? 3 : 20,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                CornerRadius = new CornerRadius(2),
            },
        };
        ((Border)grip.Child).Bind(Border.BackgroundProperty,
            new global::Avalonia.Markup.Xaml.MarkupExtensions.DynamicResourceExtension("Led.EditBrush"));
        return grip;
    }

    private static Button EditButton(string label, bool primary)
    {
        var background = new SolidColorBrush(Color.Parse(primary ? "#204C40" : "#E8EDF0"));
        var foreground = new SolidColorBrush(Color.Parse(primary ? "#FFFFFF" : "#202C35"));
        return new Button
        {
            Background = background,
            BorderBrush = new SolidColorBrush(Color.Parse(primary ? "#245748" : "#677783")),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(0),
            Content = new Border
            {
                Background = background,
                Padding = new Thickness(9, 4),
                CornerRadius = new CornerRadius(3),
                Child = new TextBlock { Text = label, Foreground = foreground, FontWeight = FontWeight.SemiBold },
            },
            [global::Avalonia.Automation.AutomationProperties.NameProperty] = label,
        };
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_drag is not { } drag)
        {
            return;
        }

        var pixel = this.PointToScreen(e.GetPosition(this));
        var dx = pixel.X - drag.Pointer.X;
        var dy = pixel.Y - drag.Pointer.Y;
        if (drag.ResizeWidth || drag.ResizeHeight)
        {
            var screen = Screens.ScreenFromPoint(Position) ?? Screens.Primary;
            var scale = screen?.Scaling ?? 1;
            if (drag.ResizeWidth)
            {
                var max = screen is null ? 2000 : (screen.WorkingArea.Right - Position.X) / scale;
                _panelWidth = Math.Clamp(drag.Width + dx / scale, MinPanelWidth, Math.Max(MinPanelWidth, max));
            }

            if (drag.ResizeHeight)
            {
                var max = screen is null ? 2000 : (screen.WorkingArea.Bottom - Position.Y) / scale - EditorHeight - 2;
                _panelHeight = Math.Clamp(drag.Height + dy / scale, MinimumHeight, Math.Max(MinimumHeight, max));
            }

            RestorePlacement();
        }
        else
        {
            Position = new PixelPoint(drag.Window.X + dx, drag.Window.Y + dy);
        }

        e.Handled = true;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_drag.HasValue)
        {
            _drag = null;
            e.Pointer.Capture(null);
            e.Handled = true;
        }
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        _drag = null;
        base.OnPointerCaptureLost(e);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (_beforeEdit.HasValue && e.Key is Key.Enter or Key.Escape)
        {
            EndEdit(e.Key == Key.Enter);
            e.Handled = true;
        }

        base.OnKeyDown(e);
    }

    private void EndEdit(bool save)
    {
        if (_beforeEdit is not { } before)
        {
            return;
        }

        _beforeEdit = null;
        _drag = null;
        _preview!.Children.Remove(_panel);
        _preview = null;
        Content = _panel;
        if (save)
        {
            _preferences = _preferences with
            {
                X = Position.X,
                Y = Position.Y,
                Width = _panelWidth,
                Height = _panelHeight,
                AutomaticSize = _automaticEdit,
                PanelOpacity = _viewModel.PanelOpacity,
            };
            RestorePlacement(snap: true);
            Persist(_preferences with { X = Position.X, Y = Position.Y });
        }
        else
        {
            Position = before;
            _viewModel.PanelOpacity = NormalizeOpacity(_preferences.PanelOpacity);
            UpdateNativeGlass();
        }

        ApplyFrame();
        RestorePlacement();
    }

    private void Persist(PanelPreferences preferences)
    {
        _preferences = preferences;
        _savePreferences?.Invoke(preferences);
    }

    private void RestorePlacement(bool snap = false)
    {
        var primary = Screens.Primary ?? (Screens.All.Count > 0 ? Screens.All[0] : null);
        if (primary is null)
        {
            return;
        }

        var island = _preferences.DynamicIsland && !_beforeEdit.HasValue;
        var saved = _beforeEdit.HasValue ? Position
            : _preferences is { X: int x, Y: int y } ? new PixelPoint(x, y) : (PixelPoint?)null;
        var screen = !island && saved is { } point ? Screens.ScreenFromPoint(point) ?? primary : primary;
        var area = screen.WorkingArea;
        var scale = screen.Scaling;
        var headerHeight = island ? Math.Max(28, (area.Y - screen.Bounds.Y) / scale - 4) : 0;
        _panel.Classes.Set("dynamic-island", island);
        _viewModel.IslandMode = island;
        _viewModel.IslandHeaderHeight = headerHeight;
        UpdateIslandWindowLevel(island);
        var editorExtra = _beforeEdit.HasValue ? EditorHeight + 2 : 0;
        var compact = island && !_viewModel.IslandExpanded;
        Width = Math.Min(compact ? 320 : _panelWidth, area.Width / scale);
        Height = compact ? headerHeight + 4 : Math.Min(_panelHeight + editorExtra, area.Height / scale) + headerHeight;

        if (island)
        {
            Position = IslandPosition(screen.Bounds, (int)Math.Ceiling(Width * scale));
            return;
        }

        if (saved is { } position)
        {
            var right = area.Right - (int)Math.Ceiling(Width * scale);
            var bottom = area.Bottom - (int)Math.Ceiling(Height * scale);
            Position = new PixelPoint(Constrain(position.X, area.X, right, snap), Constrain(position.Y, area.Y, bottom, snap));
            return;
        }

        var rect = DockLayout(area, scale, Height, Width);
        Position = new PixelPoint(rect.Left, rect.Top);
    }

    private void UpdateIslandWindowLevel(bool island)
    {
        if (!OperatingSystem.IsMacOS()
            || TryGetPlatformHandle() is not IMacOSTopLevelPlatformHandle { NSWindow: var window }
            || window == 0)
        {
            return;
        }

        if (island)
        {
            _previousWindowLevel ??= MacFloatingWindow.GetLevel(window);
            MacFloatingWindow.SetLevel(window, MacFloatingWindow.StatusWindowLevel);
        }
        else if (_previousWindowLevel is { } level)
        {
            MacFloatingWindow.SetLevel(window, level);
            _previousWindowLevel = null;
        }
    }

    internal static PixelPoint IslandPosition(global::Avalonia.PixelRect area, int width) =>
        new(area.X + Math.Max(0, (area.Width - width) / 2), area.Y);

    internal static int Constrain(int value, int min, int max, bool snap)
    {
        var clamped = Math.Clamp(value, min, Math.Max(min, max));
        if (snap && clamped - min <= 12)
        {
            return min;
        }

        return snap && max >= min && max - clamped <= 12 ? max : clamped;
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        if (!e.Handled)
        {
            _skip?.Invoke();
            Render();
        }
    }

    private double MinimumHeight => PanelHeight + (_viewModel.IsLcd ? 23 : 0);

    private void ApplyFrame()
    {
        _frame = _frames();
        _viewModel.Apply(_frame);
        UpdateNativeGlass();
        var editing = _beforeEdit.HasValue;
        var automatic = editing ? _automaticEdit : _preferences.UsesAutomaticSize();
        if (editing && !automatic)
        {
            return;
        }

        var width = automatic ? MeasureAutomaticWidth() : ValidSize(_preferences.Width, PanelWidth);
        var sessionHeight = _viewModel.IsClearList
            ? _viewModel.SessionGroups.Sum(group => 37 + (group.HasChildren ? 6 + (int)Math.Ceiling(group.Children.Count / 3.0) * 35 : 0))
            : (_viewModel.IsLcd ? 36 : 12) + (_frame.SessionRows.Count * 24);
        var automaticHeight = Math.Max(MinimumHeight, sessionHeight + 29);
        var height = automatic ? automaticHeight : ValidSize(_preferences.Height, automaticHeight);
        width = Math.Max(MinPanelWidth, width);
        height = Math.Max(MinimumHeight, height);
        if (width != _panelWidth || height != _panelHeight)
        {
            _panelWidth = width;
            _panelHeight = height;
            if (IsVisible)
            {
                RestorePlacement();
            }
        }
    }

    private double MeasureAutomaticWidth()
    {
        var font = Application.Current?.Resources["Led.FontFamily"] as FontFamily ?? FontFamily.Default;
        double TextWidth(string text, double size, bool bold = false) => new FormattedText(text, CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight, new Typeface(font, weight: bold ? FontWeight.SemiBold : FontWeight.Normal),
            size, Brushes.Black).Width;

        var width = _viewModel.SessionRows.Count == 0
            ? Math.Max(
                TextWidth(_viewModel.Feed, 13) + TextWidth(_viewModel.ElapsedText, 12) + 42,
                _viewModel.Channels.Sum(channel => TextWidth(channel.Label, 11) + 24))
            : _viewModel.SessionRows.Max(row => row.ShowEditorialHeader
                ? TextWidth(row.EditorialHeading, 16, true) + TextWidth(row.Status, 12) + 120
                : TextWidth(row.CompactLabel, 14, row.IsHeader) + TextWidth(row.Status, 12) + 76);
        if (_viewModel.IsClearList)
        {
            width = Math.Max(width, _viewModel.SessionGroups.Select(group =>
                group.Children.Select(row =>
                    TextWidth(row.StationLabel, 11, true) + TextWidth(row.Status, 10) + 26).DefaultIfEmpty(0).Max() * 3)
                .DefaultIfEmpty(0).Max() + 28);
        }

        return Math.Clamp(Math.Ceiling(width), MinPanelWidth, 900);
    }

    private void UpdateNativeGlass()
    {
        if (!OperatingSystem.IsMacOSVersionAtLeast(26) || !IsVisible)
        {
            return;
        }

        var crystal = _viewModel.ThemeId == "CrystalLiquid";
        var glassTheme = crystal || _viewModel.ThemeId == "LiquidGlass";
        if (_nativeGlass != 0 && (!glassTheme || _crystalGlass != crystal))
        {
            MacFloatingWindow.RemoveLiquidGlass(_nativeGlass);
            _nativeGlass = 0;
        }

        if (!glassTheme)
        {
            return;
        }

        // Keep even the brightest parts of the clear AppKit surface translucent.
        var opacity = _viewModel.PanelOpacity * (crystal ? 0.28 : 1);
        if (_nativeGlass == 0 && TryGetPlatformHandle() is IMacOSTopLevelPlatformHandle mac)
        {
            _nativeGlass = MacFloatingWindow.AddLiquidGlass(mac.NSWindow, ClientSize.Width, ClientSize.Height, opacity, crystal);
            _crystalGlass = crystal;
        }
        else if (_nativeGlass != 0 && opacity != _glassOpacity)
        {
            MacFloatingWindow.SetGlassOpacity(_nativeGlass, opacity);
        }

        _glassOpacity = opacity;
    }

    private static double NormalizeOpacity(double value) =>
        double.IsFinite(value) ? Math.Clamp(value, MinPanelOpacity, 1) : 1;

    private static double ValidSize(double? value, double fallback) =>
        value is { } size && double.IsFinite(size) && size > 0 ? size : fallback;

    private void Render()
    {
        ApplyFrame();
        var show = _beforeEdit.HasValue || _preferences.DynamicIsland || KeepVisible || _viewModel.HasActivity || ContextMenu?.IsOpen == true;
        if (show && !IsVisible)
        {
            Show();
            RestorePlacement();
        }
        else if (!show && IsVisible)
        {
            Hide();
        }
    }
}
