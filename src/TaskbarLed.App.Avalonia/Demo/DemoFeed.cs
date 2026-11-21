using System.Globalization;
using TaskbarLed.Core.Presentation;
using TaskbarLed.Core.Sessions;

namespace TaskbarLed.App.Avalonia.Demo;

/// <summary>
/// Three sessions built the way the hooks would build them (running, finished, running), composed by the
/// real <see cref="PanelComposer"/>. Feeds the panel until live events are wired, and the screenshot test.
/// </summary>
internal sealed class DemoFeed
{
    private readonly SessionStore _store = new();
    private readonly PanelComposer _composer;

    public DemoFeed(CultureInfo culture, DateTimeOffset start)
    {
        ArgumentNullException.ThrowIfNull(culture);
        var pt = culture.TwoLetterISOLanguageName == "pt";

        void Prompt(string id, string project, TimeSpan ago) =>
            _store.Apply(new PromptSubmitted(id, "demo", start - ago, null, project));

        Prompt("a", "webapp", TimeSpan.FromSeconds(252));
        _store.Apply(new ActivityStarted("a", "demo", start.AddSeconds(-3), pt ? "Editando scraper.py" : "Editing scraper.py"));

        Prompt("b", "api", TimeSpan.FromSeconds(767));
        _store.Apply(new ResponseCompleted("b", "demo", start, pt ? "Pronto. 24 testes passando." : "Done. All 24 tests pass."));

        Prompt("c", "site", TimeSpan.FromSeconds(63));
        _store.Apply(new ActivityStarted("c", "demo", start.AddSeconds(-5), pt ? "Rodando npm test" : "Running npm test"));

        _composer = new PanelComposer(new SessionRotation(TimeSpan.FromSeconds(8)), TickerStrings.For(culture), culture);
    }

    public PanelFrame Compose(DateTimeOffset now) => _composer.Compose(_store.Snapshot(), now);
}
