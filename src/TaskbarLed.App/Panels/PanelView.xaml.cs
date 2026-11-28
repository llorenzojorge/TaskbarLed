using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using TaskbarLed.App.Presentation;

namespace TaskbarLed.App.Panels;

/// <summary>
/// Draws the panel and runs the vertical feed: each new line drops in from the top and types itself out,
/// like the Claude chat, pushing the previous line down and out of view.
/// </summary>
public partial class PanelView : UserControl
{
    public static readonly DependencyProperty ShowSessionRowsProperty = DependencyProperty.Register(
        nameof(ShowSessionRows), typeof(bool), typeof(PanelView),
        new PropertyMetadata(false, (sender, _) => ((PanelView)sender).UpdateTickerVisibility()));

    public static readonly DependencyProperty ShowCompactTickerProperty = DependencyProperty.Register(
        nameof(ShowCompactTicker), typeof(bool), typeof(PanelView), new PropertyMetadata(true));

    public bool ShowSessionRows
    {
        get => (bool)GetValue(ShowSessionRowsProperty);
        set => SetValue(ShowSessionRowsProperty, value);
    }

    public static readonly DependencyProperty ShowActivitySummaryProperty = DependencyProperty.Register(
        nameof(ShowActivitySummary), typeof(bool), typeof(PanelView),
        new PropertyMetadata(false, (sender, _) => ((PanelView)sender).UpdateTickerVisibility()));

    public bool ShowActivitySummary
    {
        get => (bool)GetValue(ShowActivitySummaryProperty);
        set => SetValue(ShowActivitySummaryProperty, value);
    }

    public bool ShowCompactTicker => (bool)GetValue(ShowCompactTickerProperty);

    private void UpdateTickerVisibility() => SetValue(ShowCompactTickerProperty, !ShowSessionRows && !ShowActivitySummary);

    private static readonly Duration SlideDuration = new(TimeSpan.FromMilliseconds(260));
    private static readonly TimeSpan TypingBudget = TimeSpan.FromMilliseconds(650);

    private readonly DispatcherTimer _typing;
    private PanelViewModel? _viewModel;
    private TextBlock? _currentLine;
    private string _typingTarget = string.Empty;
    private int _typed;
    private int _typingStep = 1;

    public PanelView()
    {
        InitializeComponent();

        _typing = new DispatcherTimer(DispatcherPriority.Render, Dispatcher);
        _typing.Tick += (_, _) => TypeNextCharacters();

        DataContextChanged += OnDataContextChanged;
        FeedHost.SizeChanged += (_, _) => LayoutFeedLines();
        Loaded += (_, _) => ShowLine();
        Unloaded += (_, _) => _typing.Stop();
    }

    /// <summary>The edit-mode Save button was clicked.</summary>
    public event EventHandler? SaveRequested;

    /// <summary>The edit-mode Cancel button was clicked.</summary>
    public event EventHandler? CancelRequested;

    private void OnSaveClick(object sender, RoutedEventArgs e) => SaveRequested?.Invoke(this, EventArgs.Empty);

    private void OnCancelClick(object sender, RoutedEventArgs e) => CancelRequested?.Invoke(this, EventArgs.Empty);

    /// <summary>Puts the current line in the feed at once, without animation (initial state, screenshots).</summary>
    public void ShowLine()
    {
        if (_viewModel is not null)
        {
            PushLine(_viewModel.DisplayText, animate: false);
        }
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged -= OnViewModelChanged;
        }

        _viewModel = e.NewValue as PanelViewModel;
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged += OnViewModelChanged;
        }
    }

    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PanelViewModel.DisplayText) && _viewModel is not null)
        {
            PushLine(_viewModel.DisplayText, animate: IsVisible);
        }
    }

    private void PushLine(string text, bool animate)
    {
        if (_currentLine is not null && (string)_currentLine.Tag == text)
        {
            return;
        }

        var height = Math.Max(1, FeedHost.ActualHeight);

        if (_currentLine is { } previous)
        {
            _typing.Stop();
            previous.Text = (string)previous.Tag; // Finish typing before it leaves.
            if (animate)
            {
                var exit = new DoubleAnimation(height, SlideDuration) { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn } };
                exit.Completed += (_, _) => FeedHost.Children.Remove(previous);
                previous.RenderTransform.BeginAnimation(TranslateTransform.YProperty, exit);
                previous.BeginAnimation(OpacityProperty, new DoubleAnimation(0, SlideDuration));
            }
            else
            {
                FeedHost.Children.Remove(previous);
            }
        }

        var line = new TextBlock
        {
            Style = (Style)FindResource("FeedText"),
            Tag = text,
            RenderTransform = new TranslateTransform(0, animate ? -height : 0),
        };
        FeedHost.Children.Add(line);
        _currentLine = line;
        LayoutFeedLines();

        if (!animate)
        {
            line.Text = text;
            return;
        }

        line.Opacity = 0;
        var enter = new DoubleAnimation(-height, 0, SlideDuration) { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } };
        line.RenderTransform.BeginAnimation(TranslateTransform.YProperty, enter);
        line.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, SlideDuration));

        // Type it out within a fixed budget so long lines do not crawl.
        _typingTarget = text;
        _typed = 0;
        var ticks = Math.Max(1, (int)(TypingBudget.TotalMilliseconds / 16));
        _typingStep = Math.Max(1, (int)Math.Ceiling(text.Length / (double)ticks));
        _typing.Interval = TimeSpan.FromMilliseconds(Math.Clamp(TypingBudget.TotalMilliseconds / Math.Max(1, text.Length), 16, 30));
        _typing.Start();
    }

    private void TypeNextCharacters()
    {
        if (_currentLine is null)
        {
            _typing.Stop();
            return;
        }

        _typed = Math.Min(_typingTarget.Length, _typed + _typingStep);
        _currentLine.Text = _typingTarget[.._typed];
        if (_typed >= _typingTarget.Length)
        {
            _typing.Stop();
        }
    }

    // Lines fill the feed width (so long text ends in "…") and sit vertically centered.
    private void LayoutFeedLines()
    {
        foreach (var child in FeedHost.Children.OfType<TextBlock>())
        {
            child.Width = Math.Max(0, FeedHost.ActualWidth);
            var lineHeight = child.FontSize * child.FontFamily.LineSpacing;
            Canvas.SetTop(child, Math.Max(0, (FeedHost.ActualHeight - lineHeight) / 2));
        }
    }
}
