using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.Security.Claims;
using ContosoDashboard.Services;

namespace ContosoDashboard.Pages
{
    [Authorize]
    public class DocumentDownloadModel : PageModel
    {
        private readonly IDocumentService _documentService;

        public DocumentDownloadModel(IDocumentService documentService)
        {
            _documentService = documentService;
        }

        public async Task<IActionResult> OnGetAsync(int documentId, string? mode)
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier);
            if (userIdClaim == null || !int.TryParse(userIdClaim.Value, out var currentUserId))
            {
                return NotFound();
            }

            var document = await _documentService.GetByIdAsync(documentId, currentUserId);
            if (document == null)
            {
                return NotFound();
            }

            var stream = await _documentService.OpenReadStreamAsync(documentId, currentUserId);
            if (stream == null)
            {
                return NotFound();
            }

            if (string.Equals(mode, "inline", StringComparison.OrdinalIgnoreCase))
            {
                Response.Headers["Content-Disposition"] = "inline";
                return File(stream, document.FileType);
            }

            return File(stream, document.FileType, document.OriginalFileName);
        }
    }
}
