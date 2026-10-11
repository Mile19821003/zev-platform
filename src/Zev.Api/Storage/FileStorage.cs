namespace Zev.Api.Storage;

public interface IFileStorage
{
    Task<string> SaveAsync(byte[] content, string contentType, CancellationToken cancellationToken);
    Task<byte[]> ReadAsync(string key, CancellationToken cancellationToken);
    Task RemoveAsync(string key, CancellationToken cancellationToken);
}

public sealed class LocalFileStorage(IConfiguration configuration) : IFileStorage
{
    private readonly string root = Path.GetFullPath(configuration["Storage:Root"]
        ?? Path.Combine(AppContext.BaseDirectory, "storage"));

    public async Task<string> SaveAsync(byte[] content, string contentType, CancellationToken cancellationToken)
    {
        if (!ValidContent(content, contentType)) throw new InvalidDataException("Unsupported file content.");
        Directory.CreateDirectory(root);
        var extension = contentType switch
        {
            "application/pdf" => ".pdf",
            "image/png" => ".png",
            "image/jpeg" => ".jpg",
            "image/webp" => ".webp",
            _ => throw new InvalidDataException("Unsupported file type.")
        };
        var key = Guid.NewGuid().ToString("N") + extension;
        var path = Path.Combine(root, key);
        await using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None,
            81920, FileOptions.Asynchronous);
        await stream.WriteAsync(content, cancellationToken);
        return key;
    }

    public Task<byte[]> ReadAsync(string key, CancellationToken cancellationToken) =>
        File.ReadAllBytesAsync(Resolve(key), cancellationToken);

    public Task RemoveAsync(string key, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        File.Delete(Resolve(key));
        return Task.CompletedTask;
    }

    private string Resolve(string key)
    {
        if (string.IsNullOrWhiteSpace(key) || key.Length > 40 || key.Contains('/') || key.Contains('\\')
            || key.Contains("..", StringComparison.Ordinal)) throw new InvalidDataException("Invalid storage key.");
        return Path.Combine(root, key);
    }

    public static bool ValidContent(byte[] content, string contentType)
    {
        if (content.Length is 0 or > 5_000_000) return false;
        return contentType switch
        {
            "application/pdf" => content.AsSpan().StartsWith("%PDF-"u8),
            "image/png" => content.AsSpan().StartsWith(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }),
            "image/jpeg" => content.AsSpan().StartsWith(new byte[] { 255, 216, 255 }),
            "image/webp" => content.Length > 12 && content.AsSpan(0, 4).SequenceEqual("RIFF"u8)
                && content.AsSpan(8, 4).SequenceEqual("WEBP"u8),
            _ => false
        };
    }

    public static bool ValidFileName(string fileName, string contentType)
    {
        if (string.IsNullOrWhiteSpace(fileName) || fileName != Path.GetFileName(fileName)
            || fileName.Length > 255 || fileName.Contains('\0') || fileName.Contains('/')
            || fileName.Contains('\\')) return false;
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        return contentType switch
        {
            "application/pdf" => extension == ".pdf",
            "image/png" => extension == ".png",
            "image/jpeg" => extension is ".jpg" or ".jpeg",
            _ => false
        };
    }
}
