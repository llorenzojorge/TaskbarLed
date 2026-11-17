using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Threading.Channels;
using TaskbarLed.Core.Ipc;

namespace TaskbarLed.Platform.Windows.Ipc;

/// <summary>
/// Receives envelopes from <c>TaskbarLed.Hook.exe</c>: live over a named pipe restricted to the current
/// user, plus whatever the hook spooled to disk while the panel was not running.
/// </summary>
public sealed class HookEventServer : IAsyncDisposable
{
    private const int Listeners = 4;
    private const int MaxMessageBytes = 4 * 1024 * 1024;
    private static readonly TimeSpan SpoolMaxAge = TimeSpan.FromDays(1);

    private readonly Channel<HookEnvelope> _channel = Channel.CreateUnbounded<HookEnvelope>(
        new UnboundedChannelOptions { SingleReader = true });

    private readonly CancellationTokenSource _stop = new();
    private readonly List<Task> _loops = [];

    /// <summary>Envelopes in arrival order.</summary>
    public ChannelReader<HookEnvelope> Envelopes => _channel.Reader;

    /// <summary>Raised when a listener fails unexpectedly (for logging).</summary>
    public event EventHandler<Exception>? Faulted;

    public void Start()
    {
        DrainSpool();
        for (var i = 0; i < Listeners; i++)
        {
            _loops.Add(Task.Run(() => ListenAsync(_stop.Token)));
        }
    }

    /// <summary>
    /// Replays events recorded while the panel was closed, then deletes them. Includes spools redirected
    /// into an MSIX package's storage, where the hook writes when Claude Code runs inside the Claude desktop app.
    /// </summary>
    public void DrainSpool()
    {
        var spools = SpoolDirectories().Where(d => d.Exists).ToList();

        // File names start with a UTC timestamp, so name order is arrival order across folders too.
        var files = spools.SelectMany(d => FilesOf(d, "*.json")).OrderBy(f => f.Name, StringComparer.Ordinal).ToList();
        foreach (var file in files)
        {
            try
            {
                if (DateTime.UtcNow - file.CreationTimeUtc < SpoolMaxAge
                    && file.Length <= MaxMessageBytes
                    && HookEnvelope.TryDecode(File.ReadAllBytes(file.FullName), out var envelope))
                {
                    _channel.Writer.TryWrite(envelope!);
                }

                file.Delete();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // The hook may still be renaming it; it will be picked up on the next start.
            }
        }

        foreach (var orphan in spools.SelectMany(d => FilesOf(d, "*.tmp")).Where(f => DateTime.UtcNow - f.CreationTimeUtc > SpoolMaxAge))
        {
            try
            {
                orphan.Delete();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }
    }

    // A spool folder that cannot be listed is skipped: replaying old events must never stop the panel from starting.
    private static List<FileInfo> FilesOf(DirectoryInfo directory, string pattern)
    {
        try
        {
            return [.. directory.EnumerateFiles(pattern)];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    private static IEnumerable<DirectoryInfo> SpoolDirectories()
    {
        yield return new DirectoryInfo(IpcEndpoints.SpoolDirectory);

        var packages = new DirectoryInfo(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Packages"));
        IEnumerable<DirectoryInfo> packageDirectories;
        try
        {
            packageDirectories = packages.Exists ? [.. packages.EnumerateDirectories()] : [];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            yield break;
        }

        foreach (var package in packageDirectories)
        {
            yield return new DirectoryInfo(IpcEndpoints.PackagedSpoolDirectory(package.FullName));
        }
    }

    private async Task ListenAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await using var pipe = CreatePipe();
                await pipe.WaitForConnectionAsync(cancellationToken).ConfigureAwait(false);
                var message = await ReadMessageAsync(pipe, cancellationToken).ConfigureAwait(false);
                if (message is not null && HookEnvelope.TryDecode(message, out var envelope))
                {
                    await _channel.Writer.WriteAsync(envelope!, cancellationToken).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (IOException)
            {
                // Client vanished mid-write; keep listening.
            }
#pragma warning disable CA1031 // A listener must survive anything and keep serving.
            catch (Exception ex)
#pragma warning restore CA1031
            {
                Faulted?.Invoke(this, ex);
                await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private static NamedPipeServerStream CreatePipe()
    {
        // Only the signed-in user may connect.
        var security = new PipeSecurity();
        security.AddAccessRule(new PipeAccessRule(
            WindowsIdentity.GetCurrent().User!, PipeAccessRights.ReadWrite | PipeAccessRights.CreateNewInstance, AccessControlType.Allow));

        return NamedPipeServerStreamAcl.Create(
            IpcEndpoints.PipeName,
            PipeDirection.In,
            NamedPipeServerStream.MaxAllowedServerInstances,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous,
            inBufferSize: 64 * 1024,
            outBufferSize: 0,
            security);
    }

    private static async Task<byte[]?> ReadMessageAsync(Stream pipe, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[16 * 1024];
        int read;
        while ((read = await pipe.ReadAsync(chunk, cancellationToken).ConfigureAwait(false)) > 0)
        {
            if (buffer.Length + read > MaxMessageBytes)
            {
                return null;
            }

            buffer.Write(chunk, 0, read);
        }

        return buffer.ToArray();
    }

    /// <summary>
    /// Stops listening without waiting for the listener tasks. Safe to call from a UI thread
    /// that is shutting down; pending pipe connections end with the process.
    /// </summary>
    public void Stop()
    {
        if (!_stop.IsCancellationRequested)
        {
            _stop.Cancel();
        }

        _channel.Writer.TryComplete();
    }

    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync().ConfigureAwait(false);
        try
        {
            await Task.WhenAll(_loops).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }

        _channel.Writer.TryComplete();
        _stop.Dispose();
    }
}
