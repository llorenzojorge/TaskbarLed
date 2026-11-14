using TaskbarLed.Core.Ipc;
using TaskbarLed.Core.Sessions;
using TaskbarLed.Core.Sources;

namespace TaskbarLed.App.Infrastructure;

/// <summary>Moves envelopes from the IPC server through the matching <see cref="ISessionSource"/> into the store.</summary>
public sealed class EventPump
{
    private readonly Dictionary<string, ISessionSource> _sources;
    private readonly SessionStore _store;
    private readonly AppLog _log;

    public EventPump(IEnumerable<ISessionSource> sources, SessionStore store, AppLog log)
    {
        ArgumentNullException.ThrowIfNull(sources);
        _sources = sources.ToDictionary(s => s.SourceId, StringComparer.OrdinalIgnoreCase);
        _store = store;
        _log = log;
    }

    public async Task RunAsync(System.Threading.Channels.ChannelReader<HookEnvelope> envelopes, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(envelopes);
        await foreach (var envelope in envelopes.ReadAllAsync(cancellationToken).ConfigureAwait(false))
        {
            if (!_sources.TryGetValue(envelope.Source, out var source))
            {
                continue; // A hook for a source this build does not know.
            }

            try
            {
                var events = await source.TranslateAsync(envelope.Payload, envelope.ReceivedAt, envelope.Environment, cancellationToken).ConfigureAwait(false);
                _store.ApplyRange(events);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
#pragma warning disable CA1031 // One bad payload must not stop the pump.
            catch (Exception ex)
#pragma warning restore CA1031
            {
                _log.Error($"Source '{source.SourceId}' failed to translate an event.", ex);
            }
        }
    }

    /// <summary>
    /// Asks each source about its sessions that are in flight but have had no event for
    /// <paramref name="quietFor"/>: some changes (an interrupted prompt) never produce a hook.
    /// </summary>
    public async Task CheckQuietSessionsAsync(TimeSpan quietFor, CancellationToken cancellationToken)
    {
        var cutoff = DateTimeOffset.UtcNow - quietFor;
        var quiet = _store.Snapshot()
            .Where(s => s.Status is SessionStatus.Running or SessionStatus.AwaitingInput && s.LastUpdated < cutoff)
            .ToList();

        foreach (var group in quiet.GroupBy(s => s.SourceId, StringComparer.OrdinalIgnoreCase))
        {
            if (!_sources.TryGetValue(group.Key, out var source))
            {
                continue;
            }

            try
            {
                var events = await source.CheckQuietSessionsAsync([.. group], cancellationToken).ConfigureAwait(false);
                _store.ApplyRange(events);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
#pragma warning disable CA1031 // A failing check must not take the panel down; the next one retries.
            catch (Exception ex)
#pragma warning restore CA1031
            {
                _log.Error($"Source '{source.SourceId}' failed to check quiet sessions.", ex);
            }
        }
    }
}
