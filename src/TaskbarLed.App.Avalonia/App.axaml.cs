using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Platform;
using TaskbarLed.App.Avalonia.Demo;
using TaskbarLed.App.Avalonia.Panels;
using TaskbarLed.App.Avalonia.Presentation;
using TaskbarLed.Core.Ipc;

namespace TaskbarLed.App.Avalonia;

public sealed partial class App : Application
{
    public const string DisplayName = "TaskbarLed";

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
        Resources["Led.FeedPrefix"] = string.Empty;
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var culture = CultureInfo.CurrentUICulture;
            var viewModel = new PanelViewModel(culture) { Uppercase = true };
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            if (desktop.Args?.Contains("--demo", StringComparer.Ordinal) == true || !OperatingSystem.IsMacOS())
            {
                var demo = new DemoFeed(culture, DateTimeOffset.Now);
                PanelThemes.Apply(this, viewModel, "ClaudeCode");
                desktop.MainWindow = new FloatingPanelWindow(viewModel, () => demo.Compose(DateTimeOffset.Now), keepVisible: true,
                    portuguese: culture.TwoLetterISOLanguageName == "pt");
            }
            else
            {
                StartLive(desktop, culture, viewModel);
            }
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void StartLive(IClassicDesktopStyleApplicationLifetime desktop, CultureInfo culture, PanelViewModel viewModel)
    {
        Directory.CreateDirectory(IpcEndpoints.DataDirectory);
        FileStream instance;
        try
        {
            instance = File.Open(Path.Combine(IpcEndpoints.DataDirectory, "panel.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        }
        catch (IOException)
        {
            global::Avalonia.Threading.Dispatcher.UIThread.Post(() => desktop.Shutdown());
            return;
        }

        var feed = new LiveFeed(culture, CompletionSound.Play);
        var pt = culture.TwoLetterISOLanguageName == "pt";
        var strings = MacUiStrings.For(pt);
        var preferences = PanelPreferences.Load();
        var currentTheme = PanelThemes.Apply(this, viewModel, preferences.Theme);
        var panel = new FloatingPanelWindow(viewModel, () => feed.Compose(DateTimeOffset.Now), feed.Acknowledge, feed.Skip, portuguese: pt);
        panel.ConfigurePlacement(preferences, next =>
        {
            try
            {
                next.Save();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                var close = new Button { Content = "OK" };
                var error = new Window
                {
                    Title = DisplayName,
                    Width = 320,
                    Height = 115,
                    Topmost = true,
                    WindowStartupLocation = WindowStartupLocation.CenterScreen,
                    Content = new StackPanel
                    {
                        Margin = new Thickness(16),
                        Spacing = 12,
                        Children =
                        {
                            new TextBlock { Text = strings.SaveFailed,
                                TextWrapping = TextWrapping.Wrap },
                            close,
                        },
                    },
                };
                close.Click += (_, _) => error.Close();
                error.Show();
            }
        });
        desktop.MainWindow = panel;
        var cancellation = new CancellationTokenSource();
        var readers = Task.WhenAll(
            Task.Run(() => feed.ReadAsync(cancellation.Token)),
            Task.Run(() => feed.DetectProcessesAsync(cancellation.Token)),
            Task.Run(() => feed.DetectAntigravityAsync(cancellation.Token)));

        var menu = new NativeMenu();
        var panelMenu = new ContextMenu { Classes = { "taskbarled-panel-menu" } };
        panelMenu.Items.Add(new MenuItem { Header = DisplayName, IsEnabled = false, Classes = { "taskbarled-menu-heading" } });
        panelMenu.Items.Add(new Separator());
        panel.ContextMenu = panelMenu;
        var nativeThemes = new NativeMenu();
        var contextThemes = new MenuItem { Header = strings.Themes };
        var checks = new List<(PanelThemes.Theme Theme, NativeMenuItem Native, MenuItem Context)>();
        foreach (var theme in PanelThemes.All)
        {
            var native = new NativeMenuItem(theme.Name) { ToggleType = MenuItemToggleType.CheckBox };
            var context = CreateThemeMenuItem(theme);
            native.Click += (_, _) => SelectTheme(theme);
            context.Click += (_, _) => SelectTheme(theme);
            nativeThemes.Items.Add(native);
            contextThemes.Items.Add(context);
            checks.Add((theme, native, context));
        }

        void UpdateChecks()
        {
            foreach (var (theme, native, context) in checks)
            {
                native.IsChecked = context.IsChecked = theme == currentTheme;
            }
        }

        void SelectTheme(PanelThemes.Theme theme)
        {
            currentTheme = PanelThemes.Apply(this, viewModel, theme.Id);
            panel.SetTheme(currentTheme.Id);
            UpdateChecks();
        }

        void AddAction(string label, Action action)
        {
            var native = new NativeMenuItem(label);
            native.Click += (_, _) => action();
            menu.Items.Add(native);
            var context = new MenuItem { Header = label };
            context.Click += (_, _) => action();
            panelMenu.Items.Add(context);
        }

        AddAction(strings.EditPlacement, panel.BeginEdit);
        var islandLabel = pt ? "Modo Dynamic Island" : "Dynamic Island mode";
        var nativeIsland = new NativeMenuItem(islandLabel) { ToggleType = MenuItemToggleType.CheckBox };
        var contextIsland = new MenuItem { Header = islandLabel, ToggleType = MenuItemToggleType.CheckBox };
        void SetIsland(bool enabled)
        {
            panel.SetDynamicIsland(enabled);
            nativeIsland.IsChecked = contextIsland.IsChecked = enabled;
        }
        nativeIsland.IsChecked = contextIsland.IsChecked = panel.IsDynamicIsland;
        nativeIsland.Click += (_, _) => SetIsland(!panel.IsDynamicIsland);
        contextIsland.Click += (_, _) => SetIsland(!panel.IsDynamicIsland);
        AddAction(strings.ResetPlacement, () =>
        {
            SetIsland(false);
            panel.ResetPosition();
        });
        menu.Items.Add(nativeIsland);
        panelMenu.Items.Add(contextIsland);
        AddAction(strings.EnableIntegration,
            () => ConfigureHooks(pt, install: true));
        AddAction(strings.DisableIntegration,
            () => ConfigureHooks(pt, install: false));
        menu.Items.Add(new NativeMenuItem(strings.Themes) { Menu = nativeThemes });
        panelMenu.Items.Add(contextThemes);
        UpdateChecks();

        var toggle = new NativeMenuItem(strings.ShowWhenIdle) { ToggleType = MenuItemToggleType.CheckBox };
        toggle.Click += (_, _) =>
        {
            panel.KeepVisible = !panel.KeepVisible;
            toggle.IsChecked = panel.KeepVisible;
        };
        menu.Items.Add(toggle);
        AddQuitItems(menu, panelMenu, pt, () => desktop.Shutdown());
        var tray = new TrayIcon { ToolTipText = DisplayName, Icon = CreateTrayIcon(), Menu = menu, IsVisible = true };
        TrayIcon.SetIcons(this, [tray]);
        desktop.Exit += (_, _) =>
        {
            cancellation.Cancel();
            tray.Dispose();
            _ = readers.ContinueWith(task =>
            {
                _ = task.Exception;
                cancellation.Dispose();
                instance.Dispose();
            }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        };
    }

    private static void ConfigureHooks(bool portuguese, bool install)
    {
        var error = Program.ChangeHooks(install);
        var message = error is not null
            ? (MacUiStrings.For(portuguese).IntegrationFailed)
                + Environment.NewLine + error
            : install
                ? (portuguese ? "Integração ativada. Reinicie as sessões abertas do Claude Code."
                    : "Integration enabled. Restart your open Claude Code sessions.")
                : (portuguese ? "Integração desativada. Reinicie as sessões abertas do Claude Code."
                    : "Integration disabled. Restart your open Claude Code sessions.");
        var done = new Button { Content = MacUiStrings.For(portuguese).Close };
        var dialog = new Window
        {
            Title = DisplayName,
            Width = 390,
            SizeToContent = SizeToContent.Height,
            CanResize = false,
            Topmost = true,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            Content = new StackPanel
            {
                Margin = new Thickness(18),
                Spacing = 14,
                Children = { new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap }, done },
            },
        };
        done.Click += (_, _) => dialog.Close();
        dialog.Show();
        dialog.Activate();
    }

    internal static MenuItem CreateThemeMenuItem(PanelThemes.Theme theme)
    {
        var (background, foreground) = PanelThemes.PreviewColors(theme);
        return new MenuItem
        {
            Header = theme.Name,
            ToggleType = MenuItemToggleType.CheckBox,
            Classes = { "taskbarled-theme-option" },
            HeaderTemplate = new global::Avalonia.Controls.Templates.FuncDataTemplate<string>((name, _) =>
            {
                var swatch = new Border
                {
                    Width = 24, Height = 15, CornerRadius = new CornerRadius(4),
                    VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Center,
                    Background = new SolidColorBrush(Color.FromRgb(background.R, background.G, background.B)),
                    BorderBrush = new SolidColorBrush(Color.FromArgb(105, foreground.R, foreground.G, foreground.B)),
                    BorderThickness = new Thickness(1),
                    Child = new Border
                    {
                        Width = 11, Height = 2, CornerRadius = new CornerRadius(1),
                        HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Center,
                        VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Center,
                        Background = new SolidColorBrush(foreground),
                    },
                };
                return new StackPanel
                {
                    Orientation = global::Avalonia.Layout.Orientation.Horizontal, Spacing = 9,
                    Children = { swatch, new TextBlock { Text = name, VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Center } },
                };
            }),
        };
    }

    internal static void AddQuitItems(NativeMenu menu, ContextMenu panelMenu, bool portuguese, Action shutdown)
    {
        var label = MacUiStrings.For(portuguese).Quit;
        void ScheduleShutdown() => global::Avalonia.Threading.Dispatcher.UIThread.Post(shutdown,
            global::Avalonia.Threading.DispatcherPriority.Background);

        menu.Items.Add(new NativeMenuItemSeparator());
        var native = new NativeMenuItem(label);
        native.Click += (_, _) => ScheduleShutdown();
        menu.Items.Add(native);

        panelMenu.Items.Add(new Separator());
        var context = new MenuItem { Header = label, Classes = { "taskbarled-menu-quit" } };
        context.Click += (_, _) => ScheduleShutdown();
        panelMenu.Items.Add(context);
    }

    private static WindowIcon CreateTrayIcon()
    {
        using var mark = AssetLoader.Open(new Uri("avares://TaskbarLed/Assets/TaskbarLedMark.png"));
        return new WindowIcon(mark);
    }
}
