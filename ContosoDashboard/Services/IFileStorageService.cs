using Microsoft.Extensions.Options;

namespace ContosoDashboard.Services;

public interface IFileStorageService
{
    Task<string> UploadAsync(Stream fileStream, string storagePath, string contentType);

    Task DeleteAsync(string storagePath);

    Task<Stream> DownloadAsync(string storagePath);

    Task<string> GetUrlAsync(string storagePath, TimeSpan expiration);
}

public class LocalFileStorageService : IFileStorageService
{
    private readonly string _rootPath;

    public LocalFileStorageService(IOptions<FileStorageOptions> options)
    {
        var configuredRoot = options.Value.RootPath;
        _rootPath = Path.IsPathRooted(configuredRoot)
            ? configuredRoot
            : Path.Combine(AppContext.BaseDirectory, configuredRoot);
    }

    public async Task<string> UploadAsync(Stream fileStream, string storagePath, string contentType)
    {
        var fullPath = GetFullPath(storagePath);
        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        using (var destination = new FileStream(fullPath, FileMode.CreateNew, FileAccess.Write))
        {
            fileStream.Seek(0, SeekOrigin.Begin);
            await fileStream.CopyToAsync(destination);
        }

        return storagePath;
    }

    public Task DeleteAsync(string storagePath)
    {
        var fullPath = GetFullPath(storagePath);
        if (File.Exists(fullPath))
        {
            File.Delete(fullPath);
        }

        return Task.CompletedTask;
    }

    public Task<Stream> DownloadAsync(string storagePath)
    {
        var fullPath = GetFullPath(storagePath);
        Stream stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        return Task.FromResult(stream);
    }

    public Task<string> GetUrlAsync(string storagePath, TimeSpan expiration)
    {
        // Local files are never web-accessible; callers must route through the
        // authenticated DocumentDownload Razor Page rather than a direct file URL.
        return Task.FromResult($"/documents/file?storagePath={Uri.EscapeDataString(storagePath)}");
    }

    private string GetFullPath(string storagePath)
    {
        return Path.Combine(_rootPath, storagePath);
    }
}
