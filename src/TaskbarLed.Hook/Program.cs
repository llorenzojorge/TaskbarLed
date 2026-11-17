using System.IO.Pipes;
using System.Runtime.Versioning;
using System.Security.Principal;
using TaskbarLed.Core.Ipc;

namespace TaskbarLed.Hook;

/// <summary>
/// Tiny bridge called by agent hooks: <c>TaskbarLed.Hook.exe &lt;source-id&gt;</c> with the hook JSON on stdin.
/// It forwards the payload to the panel over a named pipe, or spools it to disk when the panel is not running.
/// Contract: never write to stdout/stderr, never fail, finish fast. A hook that misbehaves would slow
/// down or block the agent, which is the opposite of what this project is for.
/// </summary>
internal static class Program
{
    private const string DefaultSource = "claude-code";
    private const int MaxSpoolFiles = 500;

    private static int Main(string[] args)
    {
        try
        {
            var source = args.Length > 0 && !string.IsNullOrWhiteSpace(args[0]) ? args[0] : DefaultSource;
            var payload = ReadStdin();
            if (payload.Length == 0)
            {
                return 0;
            }

            var envelope = HookEnvelope.Encode(source, DateTimeOffset.Now, payload, CapturedEnvironment());
            if (!TrySendToPanel(envelope))
            {
                Spool(envelope);
            }
        }
#pragma warning disable CA1031 // A hook must swallow everything: any failure here would surface inside the agent.
        catch (Exception)
#pragma warning restore CA1031
        {
        }

        return 0;
    }

    private static Dictionary<string, string> CapturedEnvironment()
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var name in HookEnvelope.CapturedVariables)
        {
            if (Environment.GetEnvironmentVariable(name) is { Length: > 0 } value)
            {
                result[name] = value;
            }
        }

        return result;
    }

    // Raw bytes: hook payloads are UTF-8, and decoding through the console code page corrupts accents.
    // Callers other than Claude Code (scripts, Windows PowerShell) may add a BOM or send UTF-16; normalize to UTF-8.
    private static byte[] ReadStdin()
    {
        using var stdin = Console.OpenStandardInput();
        using var buffer = new MemoryStream();
        stdin.CopyTo(buffer);
        var bytes = buffer.ToArray();

        if (bytes is [0xEF, 0xBB, 0xBF, ..])
        {
            return bytes[3..];
        }

        if (bytes is [0xFF, 0xFE, ..])
        {
            return System.Text.Encoding.UTF8.GetBytes(System.Text.Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2));
        }

        return bytes;
    }

    private static bool TrySendToPanel(byte[] envelope)
    {
        // Checking the pipe first avoids paying the connect timeout on every tool call when the panel is closed.
        if (!File.Exists(@"\\.\pipe\" + IpcEndpoints.PipeName))
        {
            return false;
        }

        try
        {
            using var pipe = OperatingSystem.IsWindows()
                ? OpenForOwnerCheck()
                : new NamedPipeClientStream(".", IpcEndpoints.PipeName, PipeDirection.Out, PipeOptions.CurrentUserOnly);
            pipe.Connect(IpcEndpoints.ConnectTimeout);
            if (OperatingSystem.IsWindows() && !IsOurPanel(pipe))
            {
                return false;
            }

            pipe.Write(envelope);
            pipe.Flush();
            return true;
        }
        catch (Exception ex) when (ex is IOException or TimeoutException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>
    /// Pipe names are machine-wide: another account could create a pipe with our name before the panel
    /// starts and receive every payload (prompts, commands, replies). Only write to a pipe created by this
    /// user. A pipe owned by Administrators or SYSTEM is accepted as well: that is the panel started
    /// elevated, and those accounts can read this user's data anyway. <c>PipeOptions.CurrentUserOnly</c>
    /// is not used because it compares against the token's default owner, which differs between an
    /// elevated panel and a normal hook, and would send every event to the spool.
    /// </summary>
    [SupportedOSPlatform("windows")]
    private static bool IsOurPanel(NamedPipeClientStream pipe)
    {
        using var me = WindowsIdentity.GetCurrent();
        return pipe.GetAccessControl().GetOwner(typeof(SecurityIdentifier)) is SecurityIdentifier owner
            && ((me.User is { } user && owner.Equals(user))
                || owner.IsWellKnown(WellKnownSidType.BuiltinAdministratorsSid)
                || owner.IsWellKnown(WellKnownSidType.LocalSystemSid));
    }

    // Write-only, plus ReadPermissions so the owner can be read before anything is written.
    [SupportedOSPlatform("windows")]
    private static NamedPipeClientStream OpenForOwnerCheck() => new(
        ".",
        IpcEndpoints.PipeName,
        PipeAccessRights.WriteData | PipeAccessRights.ReadPermissions,
        PipeOptions.None,
        TokenImpersonationLevel.None,
        HandleInheritability.None);

    private static void Spool(byte[] envelope)
    {
        var directory = Directory.CreateDirectory(IpcEndpoints.SpoolDirectory);
        if (directory.EnumerateFiles("*.json").Take(MaxSpoolFiles).Count() >= MaxSpoolFiles)
        {
            return; // Panel has not run in a long time; do not fill the disk.
        }

        // Sortable, unique name; write-then-rename so the panel never reads a half-written file.
        var name = $"{DateTime.UtcNow:yyyyMMddHHmmssfffffff}-{Guid.NewGuid():N}";
        var temp = Path.Combine(directory.FullName, name + ".tmp");
        File.WriteAllBytes(temp, envelope);
        File.Move(temp, Path.Combine(directory.FullName, name + ".json"));
    }
}
