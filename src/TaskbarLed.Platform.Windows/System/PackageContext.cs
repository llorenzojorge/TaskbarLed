using System.Diagnostics;

namespace TaskbarLed.Platform.Windows.System;

/// <summary>
/// Detects a start from inside another app's MSIX container (for example a Claude Code session in the
/// Claude desktop app). Processes it launches may have no package identity, yet Windows still redirects
/// their writes to %APPDATA%, %LOCALAPPDATA% and HKCU into that package's private storage: settings, log
/// and the auto-start Run value would land where Windows and the next normal start never look.
/// </summary>
public static class PackageContext
{
    private static readonly TimeSpan RelaunchGuard = TimeSpan.FromSeconds(30);

    /// <summary>
    /// True when a file written to <paramref name="dataDirectory"/> (a folder under %LOCALAPPDATA%) shows up
    /// in some package's <c>LocalCache</c> instead, which is what file redirection does.
    /// </summary>
    public static bool IsRedirected(string dataDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);

        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var packages = new DirectoryInfo(Path.Combine(localAppData, "Packages"));
        var probeName = $"redirect-probe-{Environment.ProcessId}.tmp";
        var probe = Path.Combine(dataDirectory, probeName);
        var relative = Path.GetRelativePath(localAppData, probe);
        try
        {
            if (!packages.Exists || relative.StartsWith("..", StringComparison.Ordinal))
            {
                return false;
            }

            Directory.CreateDirectory(dataDirectory);
            File.WriteAllText(probe, string.Empty);
            return packages.EnumerateDirectories()
                .Any(package => File.Exists(Path.Combine(package.FullName, "LocalCache", "Local", relative)));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
        finally
        {
            try
            {
                File.Delete(probe);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }
    }

    /// <summary>
    /// Starts <paramref name="executablePath"/> again through the Windows shell, which runs it outside the
    /// container. Returns false when that was already tried moments ago (the shell did not break out, so
    /// relaunching again would loop) or when it could not be started.
    /// </summary>
    /// <param name="markerDirectory">Folder for a short-lived marker that prevents a relaunch loop.</param>
    public static bool TryRelaunchOutside(string executablePath, string markerDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(markerDirectory);

        var marker = new FileInfo(Path.Combine(markerDirectory, "relaunch.marker"));
        if (marker.Exists && DateTime.UtcNow - marker.LastWriteTimeUtc < RelaunchGuard)
        {
            return false;
        }

        try
        {
            Directory.CreateDirectory(markerDirectory);
            File.WriteAllText(marker.FullName, string.Empty);

            // explorer.exe hands the path to the running shell, which starts it outside the container.
            // Full path: a bare name is also looked up in the current directory, which here can be a
            // project folder an agent is working in.
            var explorer = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe");
            using var shell = Process.Start(new ProcessStartInfo(explorer) { ArgumentList = { executablePath }, UseShellExecute = false });
            return shell is not null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or global::System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }
}
