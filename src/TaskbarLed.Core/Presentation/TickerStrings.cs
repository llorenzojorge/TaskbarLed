using System.Globalization;

namespace TaskbarLed.Core.Presentation;

/// <summary>Localizable words used on the ticker. Add a language by adding a factory below.</summary>
public sealed record TickerStrings(
    string Idle,
    string Thinking,
    string Done,
    string DoneIn,
    string WaitingForYou)
{
    public static TickerStrings English { get; } = new(
        Idle: "Claude idle",
        Thinking: "Thinking…",
        Done: "Done",
        DoneIn: "Done in {0}",
        WaitingForYou: "Waiting for you");

    public static TickerStrings Portuguese { get; } = new(
        Idle: "Claude ocioso",
        Thinking: "Pensando…",
        Done: "Concluído",
        DoneIn: "Concluído em {0}",
        WaitingForYou: "Aguardando você");

    /// <summary>Picks the strings that match <paramref name="culture"/>, falling back to English.</summary>
    public static TickerStrings For(CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(culture);
        return culture.TwoLetterISOLanguageName switch
        {
            "pt" => Portuguese,
            _ => English,
        };
    }
}
