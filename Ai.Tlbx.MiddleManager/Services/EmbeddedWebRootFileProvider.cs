using System.Reflection;
using Ai.Tlbx.MiddleManager.Services;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Primitives;

namespace Ai.Tlbx.MiddleManager.Services;

public sealed class EmbeddedWebRootFileProvider : IFileProvider
{
    private readonly Assembly _assembly;
    private readonly Dictionary<string, ResourceInfo> _resourceMap;
    private readonly DateTimeOffset _assemblyLastModified;

    private static readonly HashSet<string> KnownExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        "html", "css", "js", "json", "txt", "xml", "svg", "png", "jpg", "jpeg", "gif", "ico",
        "woff", "woff2", "ttf", "eot", "otf", "map", "webmanifest"
    };

    public EmbeddedWebRootFileProvider(Assembly assembly, string baseNamespace)
    {
        _assembly = assembly;
        _resourceMap = BuildResourceMap(baseNamespace);
        _assemblyLastModified = GetAssemblyBuildTime();
    }

    private static DateTimeOffset GetAssemblyBuildTime()
    {
        try
        {
            var exePath = Environment.ProcessPath;
            if (!string.IsNullOrEmpty(exePath) && File.Exists(exePath))
            {
                return new DateTimeOffset(File.GetLastWriteTimeUtc(exePath), TimeSpan.Zero);
            }
        }
        catch
        {
        }

        return DateTimeOffset.UtcNow;
    }

    private Dictionary<string, ResourceInfo> BuildResourceMap(string baseNamespace)
    {
        var map = new Dictionary<string, ResourceInfo>(StringComparer.OrdinalIgnoreCase);
        var prefix = baseNamespace + ".wwwroot.";

        foreach (var name in _assembly.GetManifestResourceNames())
        {
            if (name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                var relativePath = name.Substring(prefix.Length);
                var webPath = ConvertResourceNameToPath(relativePath);

                using var stream = _assembly.GetManifestResourceStream(name);
                var length = stream?.Length ?? 0;

                map[webPath] = new ResourceInfo(name, length);
            }
        }

        return map;
    }

    private readonly record struct ResourceInfo(string Name, long Length);

    private static string ConvertResourceNameToPath(string resourceName)
    {
        var parts = resourceName.Split('.');

        for (var i = parts.Length - 1; i >= 1; i--)
        {
            if (KnownExtensions.Contains(parts[i]))
            {
                var extensionStart = i;
                if (i >= 2 && parts[i - 1] == "min")
                {
                    extensionStart = i - 1;
                }

                var pathParts = parts[..extensionStart];
                var extension = string.Join(".", parts[extensionStart..]);
                return string.Join("/", pathParts) + "." + extension;
            }
        }

        return resourceName.Replace('.', '/');
    }

    public IFileInfo GetFileInfo(string subpath)
    {
        var normalizedPath = subpath.TrimStart('/').Replace('\\', '/');

        if (_resourceMap.TryGetValue(normalizedPath, out var info))
        {
            return new EmbeddedFileInfo(_assembly, info.Name, Path.GetFileName(normalizedPath), info.Length, _assemblyLastModified);
        }

        return new NotFoundFileInfo(normalizedPath);
    }

    public IDirectoryContents GetDirectoryContents(string subpath)
    {
        var normalizedPath = subpath.TrimStart('/').Replace('\\', '/');
        if (normalizedPath.Length > 0 && !normalizedPath.EndsWith('/'))
        {
            normalizedPath += "/";
        }

        var files = _resourceMap
            .Where(kvp => kvp.Key.StartsWith(normalizedPath, StringComparison.OrdinalIgnoreCase)
                          || (string.IsNullOrEmpty(normalizedPath) && !kvp.Key.Contains('/')))
            .Select(kvp => new EmbeddedFileInfo(_assembly, kvp.Value.Name, Path.GetFileName(kvp.Key), kvp.Value.Length, _assemblyLastModified))
            .ToList<IFileInfo>();

        if (files.Count == 0 && string.IsNullOrEmpty(normalizedPath.TrimEnd('/')))
        {
            files = _resourceMap
                .Where(kvp => !kvp.Key.Contains('/'))
                .Select(kvp => new EmbeddedFileInfo(_assembly, kvp.Value.Name, Path.GetFileName(kvp.Key), kvp.Value.Length, _assemblyLastModified))
                .ToList<IFileInfo>();
        }

        return files.Count > 0
            ? new EnumerableDirectoryContents(files)
            : NotFoundDirectoryContents.Singleton;
    }

    public IChangeToken Watch(string filter)
    {
        return NullChangeToken.Singleton;
    }
}
