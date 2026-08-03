using System.IO;

namespace Lamie.Application.Common.Storage;

public interface IFileStorage
{
    Task<string> UploadPublicAsync(
        Stream content,
        string objectPath,
        string contentType,
        CancellationToken cancellationToken = default);

    Task DeleteAsync(
        string? publicUrl,
        CancellationToken cancellationToken = default);
}

