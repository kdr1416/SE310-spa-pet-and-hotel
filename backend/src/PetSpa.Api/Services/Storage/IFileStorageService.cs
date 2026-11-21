namespace PetSpa.Api.Services.Storage;

public interface IFileStorageService
{
    Task<string> UploadPhotoAsync(byte[] content, CancellationToken cancellationToken = default);
    Task DeleteAsync(string objectPath, CancellationToken cancellationToken = default);
    Task<Uri> CreateReadUrlAsync(string objectPath, CancellationToken cancellationToken = default);
}

public sealed class FileStorageException(string message, string errorCode, Exception? cause = null)
    : Exception(message, cause)
{
    public string ErrorCode { get; } = errorCode;
}
