using System.Text.Json;
using System.Text.Json.Nodes;

namespace TaskbarLed.Sources.ClaudeCode;

/// <summary>
/// Adds or removes TaskbarLed's hooks in Claude Code's user settings (<c>~/.claude/settings.json</c>).
/// Guarantees: hooks the user already has are never touched; running Install twice changes nothing;
/// a malformed settings file is never overwritten; a one-time backup is kept next to the file.
/// </summary>
public sealed class ClaudeCodeHookInstaller
{
    /// <summary>Text that identifies our hook entries inside the settings file.</summary>
    public const string Marker = "TaskbarLed.Hook";

    /// <summary>Hook events the panel listens to.</summary>
    public static IReadOnlyList<string> Events { get; } =
        ["SessionStart", "UserPromptSubmit", "PreToolUse", "PostToolUse", "Notification", "Stop", "SessionEnd"];

    private const int HookTimeoutSeconds = 5;

    private static readonly JsonDocumentOptions ReadOptions = new()
    {
        AllowTrailingCommas = true,
        CommentHandling = JsonCommentHandling.Skip,
    };

    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };

    // Claude Code hands the hook command to a shell. Inside the double quotes we write, these still mean
    // something to bash or PowerShell: a folder named with them could run a command of its own.
    private static readonly char[] ShellCharacters = ['"', '$', '`'];

    private readonly string _settingsPath;
    private readonly string _hookExecutablePath;
    private readonly string _hookCommand;

    /// <param name="settingsPath">Claude Code user settings file.</param>
    /// <param name="hookExecutablePath">Absolute path of <c>TaskbarLed.Hook.exe</c>.</param>
    public ClaudeCodeHookInstaller(string settingsPath, string hookExecutablePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(settingsPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(hookExecutablePath);
        _settingsPath = settingsPath;
        _hookExecutablePath = hookExecutablePath;
        _hookCommand = $"\"{hookExecutablePath}\" {ClaudeCodeSource.Id}";
    }

    /// <summary>Claude Code's user settings path, honoring <c>CLAUDE_CONFIG_DIR</c>.</summary>
    public static string DefaultSettingsPath
    {
        get
        {
            var configDir = Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR");
            if (string.IsNullOrWhiteSpace(configDir))
            {
                configDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude");
            }

            return Path.Combine(configDir, "settings.json");
        }
    }

    /// <summary>True when every event has our hook pointing at the current executable.</summary>
    public bool IsInstalled()
    {
        if (Load() is not { } root || root["hooks"] is not JsonObject hooks)
        {
            return false;
        }

        return Events.All(evt => hooks[evt] is JsonArray groups && groups.Any(g => OwnedCommands(g).Contains(_hookCommand)));
    }

    /// <summary>Adds our hooks (replacing older copies of them, e.g. from a previous install path).</summary>
    /// <exception cref="InvalidOperationException">
    /// The settings file is not valid JSON, or the app sits in a folder whose name a shell would interpret.
    /// </exception>
    public void Install()
    {
        if (_hookExecutablePath.IndexOfAny(ShellCharacters) >= 0)
        {
            throw new InvalidOperationException(
                $"TaskbarLed is in a folder whose name contains a quote, $ or backtick ({_hookExecutablePath}). Move it to a folder without those characters.");
        }

        var root = Load() ?? [];
        var hooks = root["hooks"] as JsonObject ?? [];
        root["hooks"] = hooks;

        foreach (var evt in Events)
        {
            var groups = hooks[evt] as JsonArray ?? [];
            hooks[evt] = groups;
            RemoveOwned(groups);
            groups.Add(new JsonObject
            {
                ["hooks"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["type"] = "command",
                        ["command"] = _hookCommand,
                        ["timeout"] = HookTimeoutSeconds,
                    },
                },
            });
        }

        Save(root);
    }

    /// <summary>Removes our hooks and any containers left empty by that.</summary>
    public void Uninstall()
    {
        if (Load() is not { } root || root["hooks"] is not JsonObject hooks)
        {
            return;
        }

        foreach (var evt in Events)
        {
            if (hooks[evt] is JsonArray groups)
            {
                RemoveOwned(groups);
                if (groups.Count == 0)
                {
                    hooks.Remove(evt);
                }
            }
        }

        if (hooks.Count == 0)
        {
            root.Remove("hooks");
        }

        Save(root);
    }

    private static IEnumerable<string> OwnedCommands(JsonNode? group) =>
        (group?["hooks"] as JsonArray ?? [])
            .Select(h => h?["command"]?.GetValueKind() == JsonValueKind.String ? h["command"]!.GetValue<string>() : null)
            .OfType<string>()
            .Where(c => c.Contains(Marker, StringComparison.OrdinalIgnoreCase));

    // Removes only our handlers. A group shared with the user's own handlers keeps theirs.
    private static void RemoveOwned(JsonArray groups)
    {
        for (var i = groups.Count - 1; i >= 0; i--)
        {
            if (groups[i]?["hooks"] is not JsonArray handlers)
            {
                continue;
            }

            for (var j = handlers.Count - 1; j >= 0; j--)
            {
                var command = handlers[j]?["command"];
                if (command?.GetValueKind() == JsonValueKind.String
                    && command.GetValue<string>().Contains(Marker, StringComparison.OrdinalIgnoreCase))
                {
                    handlers.RemoveAt(j);
                }
            }

            if (handlers.Count == 0)
            {
                groups.RemoveAt(i);
            }
        }
    }

    private JsonObject? Load()
    {
        if (!File.Exists(_settingsPath))
        {
            return null;
        }

        var text = File.ReadAllText(_settingsPath);
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        try
        {
            return JsonNode.Parse(text, documentOptions: ReadOptions) as JsonObject
                ?? throw new InvalidOperationException($"{_settingsPath} does not contain a JSON object.");
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"{_settingsPath} is not valid JSON; it was left untouched.", ex);
        }
    }

    private void Save(JsonObject root)
    {
        var directory = Path.GetDirectoryName(_settingsPath)!;
        Directory.CreateDirectory(directory);

        var backup = _settingsPath + ".taskbarled.bak";
        if (File.Exists(_settingsPath) && !File.Exists(backup))
        {
            File.Copy(_settingsPath, backup);
        }

        // Write-then-replace so Claude Code never reads a half-written settings file. When the file is a
        // symbolic link (dotfiles kept in a repository), replace what it points to and keep the link.
        var target = File.Exists(_settingsPath)
            ? File.ResolveLinkTarget(_settingsPath, returnFinalTarget: true)?.FullName ?? _settingsPath
            : _settingsPath;
        var temp = target + ".taskbarled.tmp";
        File.WriteAllText(temp, root.ToJsonString(WriteOptions));
        File.Move(temp, target, overwrite: true);
    }
}
