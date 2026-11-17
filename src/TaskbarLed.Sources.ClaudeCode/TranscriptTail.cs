using System.Text;

namespace TaskbarLed.Sources.ClaudeCode;

/// <summary>Reads the end of a JSONL transcript, so cost stays constant however long the conversation gets.</summary>
internal static class TranscriptTail
{
    /// <summary>
    /// True for a path this app is willing to open: a fully qualified local <c>.jsonl</c> file. The path comes
    /// from a hook payload, so a network location (which would also send the user's Windows credentials to
    /// that server), a device path or any other kind of file is refused.
    /// </summary>
    public static bool IsLocalTranscript(string? path) =>
        !string.IsNullOrWhiteSpace(path)
        && Path.IsPathFullyQualified(path)
        && !path.StartsWith(@"\\", StringComparison.Ordinal)
        && !path.StartsWith("//", StringComparison.Ordinal)
        && path.EndsWith(".jsonl", StringComparison.OrdinalIgnoreCase);

    /// <summary>Returns whole lines from the last <paramref name="maxBytes"/> of the file (newest last).</summary>
    public static async Task<string> ReadAsync(string path, int maxBytes, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 4096, useAsync: true);

        var start = Math.Max(0, stream.Length - maxBytes);
        stream.Seek(start, SeekOrigin.Begin);
        var buffer = new byte[stream.Length - start];
        await stream.ReadExactlyAsync(buffer, cancellationToken).ConfigureAwait(false);

        var text = Encoding.UTF8.GetString(buffer);
        if (start > 0)
        {
            // We probably landed mid-line; drop the partial first line.
            var firstNewLine = text.IndexOf('\n', StringComparison.Ordinal);
            text = firstNewLine >= 0 ? text[(firstNewLine + 1)..] : string.Empty;
        }

        return text;
    }
}
