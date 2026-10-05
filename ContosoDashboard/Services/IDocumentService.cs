using Microsoft.EntityFrameworkCore;
using ContosoDashboard.Data;
using ContosoDashboard.Models;

namespace ContosoDashboard.Services;

public class DocumentValidationException : Exception
{
    public DocumentValidationException(string message) : base(message)
    {
    }
}

public interface IDocumentService
{
    Task<Document> UploadAsync(DocumentUploadRequest request, int requestingUserId);

    Task<Document?> GetByIdAsync(int documentId, int requestingUserId);

    Task<List<Document>> GetMyDocumentsAsync(int requestingUserId, DocumentListFilter? filter = null);

    Task<List<Document>> GetProjectDocumentsAsync(int projectId, int requestingUserId);

    Task<List<Document>> GetSharedWithMeAsync(int requestingUserId);

    Task<List<Document>> SearchAsync(string query, int requestingUserId);

    Task<bool> UpdateMetadataAsync(int documentId, DocumentMetadataUpdate update, int requestingUserId);

    Task<bool> ReplaceFileAsync(int documentId, DocumentUploadRequest replacementFile, int requestingUserId);

    Task<bool> DeleteAsync(int documentId, int requestingUserId);

    Task<bool> ShareAsync(int documentId, int recipientUserId, int requestingUserId);

    Task<Stream?> OpenReadStreamAsync(int documentId, int requestingUserId);

    Task<List<Document>> GetTaskDocumentsAsync(int taskId, int requestingUserId);

    Task<bool> AttachToTaskAsync(int taskId, int documentId, int requestingUserId);

    Task<bool> DetachFromTaskAsync(int taskId, int documentId, int requestingUserId);
}

public class DocumentService : IDocumentService
{
    private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".pdf", ".doc", ".docx", ".xls", ".xlsx", ".ppt", ".pptx", ".txt", ".jpg", ".jpeg", ".png"
    };

    private readonly ApplicationDbContext _context;
    private readonly IFileStorageService _fileStorageService;
    private readonly IMalwareScanService _malwareScanService;
    private readonly INotificationService _notificationService;

    public DocumentService(
        ApplicationDbContext context,
        IFileStorageService fileStorageService,
        IMalwareScanService malwareScanService,
        INotificationService notificationService)
    {
        _context = context;
        _fileStorageService = fileStorageService;
        _malwareScanService = malwareScanService;
        _notificationService = notificationService;
    }

    public async Task<Document> UploadAsync(DocumentUploadRequest request, int requestingUserId)
    {
        if (string.IsNullOrWhiteSpace(request.Title))
        {
            throw new DocumentValidationException("Title is required.");
        }

        if (!Enum.IsDefined(typeof(DocumentCategory), request.Category))
        {
            throw new DocumentValidationException("A valid category is required.");
        }

        var extension = Path.GetExtension(request.OriginalFileName);
        if (string.IsNullOrEmpty(extension) || !AllowedExtensions.Contains(extension))
        {
            throw new DocumentValidationException("This file type is not supported.");
        }

        if (request.FileSizeBytes <= 0 || request.FileSizeBytes > DocumentConstraints.MaxFileSizeBytes)
        {
            throw new DocumentValidationException("File size must be greater than 0 and no more than 25 MB.");
        }

        if (request.ProjectId.HasValue)
        {
            var authorized = await IsProjectMemberManagerOrAdminAsync(request.ProjectId.Value, requestingUserId);
            if (!authorized)
            {
                throw new DocumentValidationException("You are not authorized to upload documents to this project.");
            }
        }

        var scanResult = await _malwareScanService.ScanAsync(request.FileStream, request.OriginalFileName);
        if (!scanResult.IsClean)
        {
            throw new DocumentValidationException($"This file was rejected by the malware scan ({scanResult.ThreatName}).");
        }

        var projectSegment = request.ProjectId.HasValue ? request.ProjectId.Value.ToString() : "personal";
        var storagePath = $"{requestingUserId}/{projectSegment}/{Guid.NewGuid():N}{extension}";

        await _fileStorageService.UploadAsync(request.FileStream, storagePath, request.FileType);

        var document = new Document
        {
            Title = request.Title,
            Description = request.Description,
            Category = request.Category,
            Tags = request.Tags,
            ProjectId = request.ProjectId,
            UploadedByUserId = requestingUserId,
            UploadDate = DateTime.UtcNow,
            UpdatedDate = DateTime.UtcNow,
            FileSizeBytes = request.FileSizeBytes,
            FileType = request.FileType,
            OriginalFileName = request.OriginalFileName,
            StoragePath = storagePath
        };

        try
        {
            _context.Documents.Add(document);
            await _context.SaveChangesAsync();
        }
        catch
        {
            await _fileStorageService.DeleteAsync(storagePath);
            throw;
        }

        _context.DocumentActivityLogEntries.Add(new DocumentActivityLogEntry
        {
            DocumentId = document.DocumentId,
            DocumentTitleSnapshot = document.Title,
            UserId = requestingUserId,
            ActionType = DocumentActivityType.Upload,
            Timestamp = DateTime.UtcNow
        });
        await _context.SaveChangesAsync();

        if (document.ProjectId.HasValue)
        {
            await NotifyProjectMembersOfNewDocumentAsync(document, requestingUserId);
        }

        return document;
    }

    public async Task<Document?> GetByIdAsync(int documentId, int requestingUserId)
    {
        var document = await _context.Documents.FirstOrDefaultAsync(d => d.DocumentId == documentId);
        if (document == null) return null;

        return await CanAccessDocumentAsync(document, requestingUserId) ? document : null;
    }

    public async Task<List<Document>> GetMyDocumentsAsync(int requestingUserId, DocumentListFilter? filter = null)
    {
        var query = _context.Documents.Where(d => d.UploadedByUserId == requestingUserId);

        query = ApplyFilter(query, filter);

        return await ApplySort(query, filter).ToListAsync();
    }

    public async Task<List<Document>> GetProjectDocumentsAsync(int projectId, int requestingUserId)
    {
        var authorized = await IsProjectMemberManagerOrAdminAsync(projectId, requestingUserId);
        if (!authorized)
        {
            return new List<Document>();
        }

        return await _context.Documents
            .Where(d => d.ProjectId == projectId)
            .OrderByDescending(d => d.UploadDate)
            .ToListAsync();
    }

    public async Task<List<Document>> GetSharedWithMeAsync(int requestingUserId)
    {
        return await _context.DocumentShares
            .Where(ds => ds.SharedWithUserId == requestingUserId)
            .OrderByDescending(ds => ds.SharedDate)
            .Select(ds => ds.Document)
            .ToListAsync();
    }

    public async Task<List<Document>> SearchAsync(string query, int requestingUserId)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return new List<Document>();
        }

        var isAdministrator = await IsAdministratorAsync(requestingUserId);

        var managedProjectIds = await _context.Projects
            .Where(p => p.ProjectManagerId == requestingUserId)
            .Select(p => p.ProjectId)
            .ToListAsync();

        var memberProjectIds = await _context.ProjectMembers
            .Where(pm => pm.UserId == requestingUserId)
            .Select(pm => pm.ProjectId)
            .ToListAsync();

        var sharedDocumentIds = await _context.DocumentShares
            .Where(ds => ds.SharedWithUserId == requestingUserId)
            .Select(ds => ds.DocumentId)
            .ToListAsync();

        var accessibleProjectIds = managedProjectIds.Union(memberProjectIds).ToHashSet();

        var candidates = await _context.Documents
            .Include(d => d.UploadedByUser)
            .Include(d => d.Project)
            .Where(d =>
                d.Title.Contains(query) ||
                (d.Description != null && d.Description.Contains(query)) ||
                (d.Tags != null && d.Tags.Contains(query)) ||
                d.UploadedByUser.DisplayName.Contains(query) ||
                (d.Project != null && d.Project.Name.Contains(query)))
            .ToListAsync();

        return candidates
            .Where(d => isAdministrator
                || d.UploadedByUserId == requestingUserId
                || sharedDocumentIds.Contains(d.DocumentId)
                || (d.ProjectId.HasValue && accessibleProjectIds.Contains(d.ProjectId.Value)))
            .OrderByDescending(d => d.UploadDate)
            .ToList();
    }

    public async Task<bool> UpdateMetadataAsync(int documentId, DocumentMetadataUpdate update, int requestingUserId)
    {
        var document = await _context.Documents.FirstOrDefaultAsync(d => d.DocumentId == documentId);
        if (document == null) return false;

        if (document.UploadedByUserId != requestingUserId)
        {
            return false;
        }

        document.Title = update.Title;
        document.Description = update.Description;
        document.Category = update.Category;
        document.Tags = update.Tags;
        document.UpdatedDate = DateTime.UtcNow;

        await _context.SaveChangesAsync();
        return true;
    }

    public async Task<bool> ReplaceFileAsync(int documentId, DocumentUploadRequest replacementFile, int requestingUserId)
    {
        var document = await _context.Documents.FirstOrDefaultAsync(d => d.DocumentId == documentId);
        if (document == null) return false;

        if (document.UploadedByUserId != requestingUserId)
        {
            return false;
        }

        var extension = Path.GetExtension(replacementFile.OriginalFileName);
        if (string.IsNullOrEmpty(extension) || !AllowedExtensions.Contains(extension))
        {
            throw new DocumentValidationException("This file type is not supported.");
        }

        if (replacementFile.FileSizeBytes <= 0 || replacementFile.FileSizeBytes > DocumentConstraints.MaxFileSizeBytes)
        {
            throw new DocumentValidationException("File size must be greater than 0 and no more than 25 MB.");
        }

        var scanResult = await _malwareScanService.ScanAsync(replacementFile.FileStream, replacementFile.OriginalFileName);
        if (!scanResult.IsClean)
        {
            throw new DocumentValidationException($"This file was rejected by the malware scan ({scanResult.ThreatName}).");
        }

        var projectSegment = document.ProjectId.HasValue ? document.ProjectId.Value.ToString() : "personal";
        var newStoragePath = $"{document.UploadedByUserId}/{projectSegment}/{Guid.NewGuid():N}{extension}";

        await _fileStorageService.UploadAsync(replacementFile.FileStream, newStoragePath, replacementFile.FileType);

        var oldStoragePath = document.StoragePath;

        document.StoragePath = newStoragePath;
        document.FileType = replacementFile.FileType;
        document.FileSizeBytes = replacementFile.FileSizeBytes;
        document.OriginalFileName = replacementFile.OriginalFileName;
        document.UpdatedDate = DateTime.UtcNow;

        try
        {
            await _context.SaveChangesAsync();
        }
        catch
        {
            await _fileStorageService.DeleteAsync(newStoragePath);
            throw;
        }

        await _fileStorageService.DeleteAsync(oldStoragePath);
        return true;
    }

    public async Task<bool> DeleteAsync(int documentId, int requestingUserId)
    {
        var document = await _context.Documents.FirstOrDefaultAsync(d => d.DocumentId == documentId);
        if (document == null) return false;

        var isUploader = document.UploadedByUserId == requestingUserId;
        var isAdministrator = await IsAdministratorAsync(requestingUserId);
        var isProjectManager = document.ProjectId.HasValue && await IsProjectManagerAsync(document.ProjectId.Value, requestingUserId);

        if (!isUploader && !isAdministrator && !isProjectManager)
        {
            return false;
        }

        var titleSnapshot = document.Title;
        var storagePath = document.StoragePath;

        _context.Documents.Remove(document);

        _context.DocumentActivityLogEntries.Add(new DocumentActivityLogEntry
        {
            DocumentId = null,
            DocumentTitleSnapshot = titleSnapshot,
            UserId = requestingUserId,
            ActionType = DocumentActivityType.Delete,
            Timestamp = DateTime.UtcNow
        });

        await _context.SaveChangesAsync();
        await _fileStorageService.DeleteAsync(storagePath);

        return true;
    }

    public async Task<bool> ShareAsync(int documentId, int recipientUserId, int requestingUserId)
    {
        var document = await _context.Documents.FirstOrDefaultAsync(d => d.DocumentId == documentId);
        if (document == null) return false;

        if (document.UploadedByUserId != requestingUserId)
        {
            return false;
        }

        var existingShare = await _context.DocumentShares
            .FirstOrDefaultAsync(ds => ds.DocumentId == documentId && ds.SharedWithUserId == recipientUserId);

        if (existingShare != null)
        {
            existingShare.SharedDate = DateTime.UtcNow;
        }
        else
        {
            _context.DocumentShares.Add(new DocumentShare
            {
                DocumentId = documentId,
                SharedWithUserId = recipientUserId,
                SharedByUserId = requestingUserId,
                SharedDate = DateTime.UtcNow
            });
        }

        _context.DocumentActivityLogEntries.Add(new DocumentActivityLogEntry
        {
            DocumentId = document.DocumentId,
            DocumentTitleSnapshot = document.Title,
            UserId = requestingUserId,
            ActionType = DocumentActivityType.Share,
            Timestamp = DateTime.UtcNow
        });

        await _context.SaveChangesAsync();

        await _notificationService.CreateNotificationAsync(new Notification
        {
            UserId = recipientUserId,
            Title = "A document was shared with you",
            Message = $"\"{document.Title}\" was shared with you.",
            Type = NotificationType.DocumentShared,
            Priority = NotificationPriority.Informational
        });

        return true;
    }

    public async Task<Stream?> OpenReadStreamAsync(int documentId, int requestingUserId)
    {
        var document = await _context.Documents.FirstOrDefaultAsync(d => d.DocumentId == documentId);
        if (document == null) return null;

        if (!await CanAccessDocumentAsync(document, requestingUserId))
        {
            return null;
        }

        var stream = await _fileStorageService.DownloadAsync(document.StoragePath);

        _context.DocumentActivityLogEntries.Add(new DocumentActivityLogEntry
        {
            DocumentId = document.DocumentId,
            DocumentTitleSnapshot = document.Title,
            UserId = requestingUserId,
            ActionType = DocumentActivityType.Download,
            Timestamp = DateTime.UtcNow
        });
        await _context.SaveChangesAsync();

        return stream;
    }

    public async Task<List<Document>> GetTaskDocumentsAsync(int taskId, int requestingUserId)
    {
        var task = await _context.Tasks.FirstOrDefaultAsync(t => t.TaskId == taskId);
        if (task == null) return new List<Document>();

        var documentIds = await _context.TaskDocuments
            .Where(td => td.TaskId == taskId)
            .Select(td => td.DocumentId)
            .ToListAsync();

        var documents = await _context.Documents
            .Include(d => d.Project)
            .Where(d => documentIds.Contains(d.DocumentId))
            .ToListAsync();

        var accessible = new List<Document>();
        foreach (var document in documents)
        {
            if (await CanAccessDocumentAsync(document, requestingUserId))
            {
                accessible.Add(document);
            }
        }

        return accessible;
    }

    public async Task<bool> AttachToTaskAsync(int taskId, int documentId, int requestingUserId)
    {
        var task = await _context.Tasks.FirstOrDefaultAsync(t => t.TaskId == taskId);
        if (task == null) return false;

        var document = await _context.Documents.FirstOrDefaultAsync(d => d.DocumentId == documentId);
        if (document == null) return false;

        if (!await CanAccessDocumentAsync(document, requestingUserId))
        {
            return false;
        }

        var alreadyAttached = await _context.TaskDocuments
            .AnyAsync(td => td.TaskId == taskId && td.DocumentId == documentId);

        if (alreadyAttached) return true;

        if (!document.ProjectId.HasValue && task.ProjectId.HasValue)
        {
            document.ProjectId = task.ProjectId;
        }

        _context.TaskDocuments.Add(new TaskDocument
        {
            TaskId = taskId,
            DocumentId = documentId,
            AttachedByUserId = requestingUserId,
            AttachedDate = DateTime.UtcNow
        });

        await _context.SaveChangesAsync();
        return true;
    }

    public async Task<bool> DetachFromTaskAsync(int taskId, int documentId, int requestingUserId)
    {
        var link = await _context.TaskDocuments
            .FirstOrDefaultAsync(td => td.TaskId == taskId && td.DocumentId == documentId);
        if (link == null) return false;

        var document = await _context.Documents.FirstOrDefaultAsync(d => d.DocumentId == documentId);
        var isUploader = document != null && document.UploadedByUserId == requestingUserId;
        var isAdministrator = await IsAdministratorAsync(requestingUserId);
        var attachedByRequester = link.AttachedByUserId == requestingUserId;

        if (!isUploader && !isAdministrator && !attachedByRequester)
        {
            return false;
        }

        _context.TaskDocuments.Remove(link);
        await _context.SaveChangesAsync();
        return true;
    }

    private async Task<bool> CanAccessDocumentAsync(Document document, int requestingUserId)
    {
        if (document.UploadedByUserId == requestingUserId) return true;

        if (await IsAdministratorAsync(requestingUserId)) return true;

        if (document.ProjectId.HasValue && await IsProjectManagerAsync(document.ProjectId.Value, requestingUserId))
        {
            return true;
        }

        var isSharedWithUser = await _context.DocumentShares
            .AnyAsync(ds => ds.DocumentId == document.DocumentId && ds.SharedWithUserId == requestingUserId);

        return isSharedWithUser;
    }

    private async Task<bool> IsAdministratorAsync(int userId)
    {
        var user = await _context.Users.FindAsync(userId);
        return user != null && user.Role == UserRole.Administrator;
    }

    private async Task<bool> IsProjectManagerAsync(int projectId, int userId)
    {
        var project = await _context.Projects.FindAsync(projectId);
        return project != null && project.ProjectManagerId == userId;
    }

    private async Task<bool> IsProjectMemberManagerOrAdminAsync(int projectId, int userId)
    {
        if (await IsAdministratorAsync(userId)) return true;
        if (await IsProjectManagerAsync(projectId, userId)) return true;

        return await _context.ProjectMembers
            .AnyAsync(pm => pm.ProjectId == projectId && pm.UserId == userId);
    }

    private async Task NotifyProjectMembersOfNewDocumentAsync(Document document, int uploaderUserId)
    {
        if (!document.ProjectId.HasValue) return;

        var project = await _context.Projects.FindAsync(document.ProjectId.Value);
        if (project == null) return;

        var memberUserIds = await _context.ProjectMembers
            .Where(pm => pm.ProjectId == document.ProjectId.Value)
            .Select(pm => pm.UserId)
            .ToListAsync();

        var recipientIds = memberUserIds
            .Append(project.ProjectManagerId)
            .Distinct()
            .Where(id => id != uploaderUserId);

        foreach (var recipientId in recipientIds)
        {
            await _notificationService.CreateNotificationAsync(new Notification
            {
                UserId = recipientId,
                Title = "New document added to project",
                Message = $"\"{document.Title}\" was added to {project.Name}.",
                Type = NotificationType.DocumentAddedToProject,
                Priority = NotificationPriority.Informational
            });
        }
    }

    private static IQueryable<Document> ApplyFilter(IQueryable<Document> query, DocumentListFilter? filter)
    {
        if (filter == null) return query;

        if (filter.Category.HasValue)
        {
            query = query.Where(d => d.Category == filter.Category.Value);
        }

        if (filter.ProjectId.HasValue)
        {
            query = query.Where(d => d.ProjectId == filter.ProjectId.Value);
        }

        if (filter.FromDate.HasValue)
        {
            query = query.Where(d => d.UploadDate >= filter.FromDate.Value);
        }

        if (filter.ToDate.HasValue)
        {
            query = query.Where(d => d.UploadDate <= filter.ToDate.Value);
        }

        return query;
    }

    private static IQueryable<Document> ApplySort(IQueryable<Document> query, DocumentListFilter? filter)
    {
        var sortBy = filter?.SortBy?.ToLowerInvariant();
        var descending = filter?.SortDescending ?? true;

        return sortBy switch
        {
            "title" => descending ? query.OrderByDescending(d => d.Title) : query.OrderBy(d => d.Title),
            "category" => descending ? query.OrderByDescending(d => d.Category) : query.OrderBy(d => d.Category),
            "filesizebytes" or "size" => descending ? query.OrderByDescending(d => d.FileSizeBytes) : query.OrderBy(d => d.FileSizeBytes),
            _ => descending ? query.OrderByDescending(d => d.UploadDate) : query.OrderBy(d => d.UploadDate)
        };
    }
}
