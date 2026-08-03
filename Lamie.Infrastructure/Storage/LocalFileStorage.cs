using Lamie.Application.Common.Storage;
using Lamie.Infrastructure.Options;

namespace Lamie.Infrastructure.Storage;

public sealed class LocalFileStorage : IFileStorage
{
    private readonly string _rootPath;
    private readonly string _rootPathPrefix;
    private readonly string _publicBasePath;

    public LocalFileStorage(LocalStorageOptions options, string rootPath)
    {
        ArgumentNullException.ThrowIfNull(options);

        _rootPath = Path.GetFullPath(
            string.IsNullOrWhiteSpace(rootPath)
                ? throw new InvalidOperationException("Local storage root path is required.")
                : rootPath);
        _rootPathPrefix = _rootPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        _publicBasePath = NormalizePublicBasePath(options.PublicBasePath);
    }

    public async Task<string> UploadPublicAsync(
        Stream content,
        string objectPath,
        string contentType,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);
        if (!content.CanRead)
        {
            throw new InvalidOperationException("The uploaded content stream is not readable.");
        }

        var pathSegments = NormalizeObjectPath(objectPath);
        var destinationPath = ResolveDestinationPath(pathSegments);

        var destinationDirectory = Path.GetDirectoryName(destinationPath)
            ?? throw new InvalidOperationException("The destination directory could not be resolved.");
        Directory.CreateDirectory(destinationDirectory);

        var temporaryPath = $"{destinationPath}.tmp-{Guid.NewGuid():N}";
        try
        {
            await using (var output = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 81920,
                options: FileOptions.Asynchronous))
            {
                await content.CopyToAsync(output, cancellationToken);
            }

            File.Move(temporaryPath, destinationPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }

        var publicObjectPath = string.Join('/', pathSegments.Select(Uri.EscapeDataString));
        return $"{_publicBasePath}/{publicObjectPath}";
    }

    public Task DeleteAsync(
        string? publicUrl,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(publicUrl))
        {
            return Task.CompletedTask;
        }

        var urlPath = Uri.TryCreate(publicUrl, UriKind.Absolute, out var absoluteUri)
            ? absoluteUri.AbsolutePath
            : publicUrl.Split('?', '#')[0];
        var expectedPrefix = $"{_publicBasePath}/";

        if (!urlPath.StartsWith(expectedPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return Task.CompletedTask;
        }

        var encodedObjectPath = urlPath[expectedPrefix.Length..];
        var objectPath = string.Join(
            '/',
            encodedObjectPath
                .Split('/', StringSplitOptions.RemoveEmptyEntries)
                .Select(Uri.UnescapeDataString));
        var destinationPath = ResolveDestinationPath(NormalizeObjectPath(objectPath));

        if (File.Exists(destinationPath))
        {
            File.Delete(destinationPath);
            DeleteEmptyParentDirectories(Path.GetDirectoryName(destinationPath));
        }

        return Task.CompletedTask;
    }

    public static string ResolveRootPath(string configuredRootPath, string contentRootPath)
    {
        if (string.IsNullOrWhiteSpace(configuredRootPath))
        {
            throw new InvalidOperationException("LocalStorage:RootPath is required.");
        }

        if (string.IsNullOrWhiteSpace(contentRootPath))
        {
            throw new InvalidOperationException("The application content root path is required.");
        }

        return Path.GetFullPath(
            Path.IsPathRooted(configuredRootPath)
                ? configuredRootPath
                : Path.Combine(contentRootPath, configuredRootPath));
    }

    public static string NormalizePublicBasePath(string publicBasePath)
    {
        if (string.IsNullOrWhiteSpace(publicBasePath))
        {
            throw new InvalidOperationException("LocalStorage:PublicBasePath is required.");
        }

        var normalized = publicBasePath.Trim().Replace('\\', '/').Trim('/');
        if (string.IsNullOrWhiteSpace(normalized)
            || normalized.Split('/', StringSplitOptions.RemoveEmptyEntries)
                .Any(segment => segment is "." or ".."))
        {
            throw new InvalidOperationException("LocalStorage:PublicBasePath is invalid.");
        }

        return $"/{normalized}";
    }

    private static string[] NormalizeObjectPath(string objectPath)
    {
        if (string.IsNullOrWhiteSpace(objectPath))
        {
            throw new InvalidOperationException("Object path is required.");
        }

        var invalidFileNameCharacters = Path.GetInvalidFileNameChars();
        var segments = objectPath
            .Replace('\\', '/')
            .Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Select(segment => segment.Trim())
            .ToArray();

        if (segments.Length == 0 || segments.Any(segment => segment is "." or ".."))
        {
            throw new InvalidOperationException("Object path is invalid.");
        }

        for (var index = 0; index < segments.Length; index++)
        {
            var sanitized = new string(
                segments[index]
                    .Select(character => invalidFileNameCharacters.Contains(character) ? '-' : character)
                    .ToArray());

            if (string.IsNullOrWhiteSpace(sanitized) || sanitized is "." or "..")
            {
                throw new InvalidOperationException("Object path contains an invalid segment.");
            }

            segments[index] = sanitized;
        }

        return segments;
    }

    private string ResolveDestinationPath(string[] pathSegments)
    {
        var destinationPath = Path.GetFullPath(
            Path.Combine(_rootPath, Path.Combine(pathSegments)));

        if (!destinationPath.StartsWith(_rootPathPrefix, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("The resolved file path is outside the local storage root.");
        }

        return destinationPath;
    }

    private void DeleteEmptyParentDirectories(string? directoryPath)
    {
        while (!string.IsNullOrWhiteSpace(directoryPath)
               && directoryPath.StartsWith(_rootPathPrefix, StringComparison.OrdinalIgnoreCase)
               && Directory.Exists(directoryPath)
               && !Directory.EnumerateFileSystemEntries(directoryPath).Any())
        {
            Directory.Delete(directoryPath);
            directoryPath = Path.GetDirectoryName(directoryPath);
        }
    }
}
