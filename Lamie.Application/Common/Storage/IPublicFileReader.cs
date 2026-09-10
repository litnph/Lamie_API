namespace Lamie.Application.Common.Storage;

public sealed record StoredPublicFile(
    byte[] Bytes,
    string ContentType,
    string FileName);

public interface IPublicFileReader
{
    Task<StoredPublicFile?> ReadPublicAsync(
        string? publicUrl,
        CancellationToken cancellationToken = default);
}
