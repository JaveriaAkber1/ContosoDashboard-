using ContosoDashboard.Models;

namespace ContosoDashboard.Services;

public static class DocumentConstraints
{
    public const long MaxFileSizeBytes = 25 * 1024 * 1024; // 25 MB
}

public class DocumentUploadRequest
{
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public DocumentCategory Category { get; set; }
    public string? Tags { get; set; }
    public int? ProjectId { get; set; }
    public Stream FileStream { get; set; } = Stream.Null;
    public string OriginalFileName { get; set; } = string.Empty;
    public string FileType { get; set; } = string.Empty;
    public long FileSizeBytes { get; set; }
}

public class DocumentListFilter
{
    public DocumentCategory? Category { get; set; }
    public int? ProjectId { get; set; }
    public DateTime? FromDate { get; set; }
    public DateTime? ToDate { get; set; }
    public string? SortBy { get; set; }
    public bool SortDescending { get; set; }
}

public class DocumentMetadataUpdate
{
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public DocumentCategory Category { get; set; }
    public string? Tags { get; set; }
}
