using System.Collections.Concurrent;

namespace Ai.Tlbx.MidTerm.Services;

/// <summary>
/// Tracks file paths detected in terminal output, allowing access only to paths
/// that have been seen in a session's terminal output or are within the session's
/// working directory.
/// </summary>
public sealed class FileRadarAllowlistService
{
    private const int MaxPathsPerSession = 1000;

    private readonly ConcurrentDictionary<string, HashSet<string>> _allowlists = new();

    public void RegisterPath(string sessionId, string path)
    {
        var normalizedPath = NormalizePath(path);
        if (string.IsNullOrEmpty(normalizedPath)) return;

        var allowlist = _allowlists.GetOrAdd(sessionId, _ => new HashSet<string>(StringComparer.OrdinalIgnoreCase));

        lock (allowlist)
        {
            if (allowlist.Count >= MaxPathsPerSession)
            {
                var firstKey = allowlist.FirstOrDefault();
                if (firstKey is not null)
                {
                    allowlist.Remove(firstKey);
                }
            }
            allowlist.Add(normalizedPath);
        }
    }

    public void RegisterPaths(string sessionId, IEnumerable<string> paths)
    {
        foreach (var path in paths)
        {
            RegisterPath(sessionId, path);
        }
    }

    public bool IsPathAllowed(string sessionId, string path, string? workingDirectory)
    {
        var normalizedPath = NormalizePath(path);
        if (string.IsNullOrEmpty(normalizedPath)) return false;

        // Check if path is within the working directory tree
        if (!string.IsNullOrEmpty(workingDirectory))
        {
            var normalizedWorkDir = NormalizePath(workingDirectory);
            if (!string.IsNullOrEmpty(normalizedWorkDir) && IsUnderDirectory(normalizedPath, normalizedWorkDir))
            {
                return true;
            }
        }

        // Check if path is in the session's allowlist
        if (_allowlists.TryGetValue(sessionId, out var allowlist))
        {
            lock (allowlist)
            {
                if (allowlist.Contains(normalizedPath))
                {
                    return true;
                }

                // Also check parent directories of the path (for directory listings)
                var parent = Path.GetDirectoryName(normalizedPath);
                while (!string.IsNullOrEmpty(parent))
                {
                    if (allowlist.Contains(parent))
                    {
                        return true;
                    }
                    parent = Path.GetDirectoryName(parent);
                }
            }
        }

        return false;
    }

    public void ClearSession(string sessionId)
    {
        _allowlists.TryRemove(sessionId, out _);
    }

    private static string? NormalizePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;

        try
        {
            return Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        catch
        {
            return null;
        }
    }

    private static bool IsUnderDirectory(string path, string directory)
    {
        var normalizedDir = directory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return path.StartsWith(normalizedDir + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            || path.Equals(normalizedDir, StringComparison.OrdinalIgnoreCase);
    }
}
