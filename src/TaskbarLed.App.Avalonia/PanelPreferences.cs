using System.Text.Json;
using TaskbarLed.Core.Ipc;

namespace TaskbarLed.App.Avalonia;

internal sealed record PanelPreferences(string Theme = "ClaudeCode", int? X = null, int? Y = null, double Width = 420, bool? AutomaticSize = null, double? Height = null, double PanelOpacity = 1, bool DynamicIsland = false)
{
    public bool UsesAutomaticSize() => AutomaticSize ?? Width == 420;

    private static string SettingsPath => Path.Combine(IpcEndpoints.DataDirectory, "panel.json");

    public static PanelPreferences Load()
    {
        try
        {
            return File.Exists(SettingsPath)
                ? JsonSerializer.Deserialize<PanelPreferences>(File.ReadAllText(SettingsPath)) ?? new()
                : new();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return new();
        }
    }

    public void Save()
    {
        var temp = SettingsPath + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(this));
        File.Move(temp, SettingsPath, overwrite: true);
    }
}
