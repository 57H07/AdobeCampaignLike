using CampaignEngine.Application.DTOs.Campaigns;
using CampaignEngine.Application.Interfaces;
using CampaignEngine.Application.Interfaces.Repositories;
using CampaignEngine.Domain.Entities;
using CampaignEngine.Domain.Exceptions;
using CampaignEngine.Infrastructure.Configuration;
using Mapster;
using Microsoft.Extensions.Options;

namespace CampaignEngine.Infrastructure.Attachments;

/// <summary>
/// Manages campaign attachment metadata and file storage.
///
/// US-028: Static and dynamic attachment management.
///
/// Coordinates:
///   1. Attachment validation (type whitelist + size limits).
///   2. File upload to UNC file share.
///   3. Persistence of CampaignAttachment DB record.
///
/// Business rules:
///   BR-1: Extension whitelist: PDF, DOCX, XLSX, PNG, JPG.
///   BR-2: Max 10 MB per file.
///   BR-3: Max 25 MB total per campaign send.
///   BR-4: Dynamic attachments store field name, not file content.
/// </summary>
public sealed class AttachmentService : IAttachmentService
{
    private readonly IAttachmentRepository _attachmentRepository;
    private readonly ICampaignRepository _campaignRepository;
    private readonly IAttachmentValidationService _validationService;
    private readonly IFileUploadService _fileUploadService;
    private readonly IUnitOfWork _unitOfWork;
    private readonly AttachmentStorageOptions _storageOptions;
    private readonly IAppLogger<AttachmentService> _logger;

    public AttachmentService(
        IAttachmentRepository attachmentRepository,
        ICampaignRepository campaignRepository,
        IAttachmentValidationService validationService,
        IFileUploadService fileUploadService,
        IUnitOfWork unitOfWork,
        IOptions<AttachmentStorageOptions> storageOptions,
        IAppLogger<AttachmentService> logger)
    {
        _attachmentRepository = attachmentRepository;
        _campaignRepository = campaignRepository;
        _validationService = validationService;
        _fileUploadService = fileUploadService;
        _unitOfWork = unitOfWork;
        _storageOptions = storageOptions.Value;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<CampaignAttachmentDto> UploadStaticAsync(
        Guid campaignId,
        string fileName,
        byte[] content,
        CancellationToken cancellationToken = default)
    {
        // Verify campaign exists
        var campaign = await _campaignRepository.GetByIdAsync(campaignId, cancellationToken);
        if (campaign is null)
            throw new NotFoundException("Campaign", campaignId);

        // Validate file type and size (BR-1, BR-2)
        var fileValidation = _validationService.ValidateFile(fileName, content.Length);
        if (!fileValidation.IsValid)
            throw new ValidationException(new Dictionary<string, string[]>
            {
                ["file"] = [fileValidation.ErrorMessage!]
            });

        // Upload to file share first — if this fails we don't have to clean up the DB record
        var storedPath = await _fileUploadService.UploadAsync(campaignId, fileName, content, cancellationToken);

        var contentType = GetContentType(Path.GetExtension(fileName));
        var attachment = new CampaignAttachment
        {
            CampaignId = campaignId,
            FileName = fileName,
            FilePath = storedPath,
            FileSizeBytes = content.Length,
            ContentType = contentType,
            IsDynamic = false
        };

        // Fix #7: serialize the quota check + insert to eliminate the TOCTOU window
        // where two concurrent uploads can each observe "room remaining" and together
        // exceed the 25 MB BR-3 limit. UPDLOCK+HOLDLOCK on the aggregate scan acquires
        // range locks keyed by CampaignId so sibling uploads for the same campaign
        // serialize while uploads for other campaigns remain unblocked.
        await _unitOfWork.BeginTransactionAsync(cancellationToken);
        try
        {
            await _unitOfWork.ExecuteSqlRawAsync(
                """
                SELECT ISNULL(SUM(FileSizeBytes), 0)
                FROM CampaignAttachments WITH (UPDLOCK, HOLDLOCK)
                WHERE CampaignId = {0} AND IsDynamic = 0
                """,
                cancellationToken,
                campaignId);

            var existingTotal = await _attachmentRepository.GetTotalFileSizeByCampaignAsync(campaignId, cancellationToken);
            var newTotal = existingTotal + content.Length;
            var totalValidation = _validationService.ValidateTotalSize(newTotal);
            if (!totalValidation.IsValid)
            {
                await _unitOfWork.RollbackTransactionAsync(cancellationToken);
                // File was written to share but rejected by quota — clean up the uploaded blob
                try { await _fileUploadService.DeleteAsync(storedPath); }
                catch (Exception ex)
                {
                    _logger.LogWarning(
                        "Failed to delete orphaned upload '{Path}' after quota rejection: {Error}",
                        storedPath, ex.Message);
                }
                throw new ValidationException(new Dictionary<string, string[]>
                {
                    ["file"] = [totalValidation.ErrorMessage!]
                });
            }

            await _attachmentRepository.AddAsync(attachment, cancellationToken);
            await _unitOfWork.CommitTransactionAsync(cancellationToken);
        }
        catch (ValidationException)
        {
            throw;
        }
        catch
        {
            await _unitOfWork.RollbackTransactionAsync(cancellationToken);
            try { await _fileUploadService.DeleteAsync(storedPath); } catch { /* best effort */ }
            throw;
        }

        _logger.LogInformation(
            "Static attachment created: Campaign={CampaignId}, File={FileName}, Size={Size} bytes",
            campaignId, fileName, content.Length);

        return attachment.Adapt<CampaignAttachmentDto>();
    }

    /// <inheritdoc />
    public async Task<CampaignAttachmentDto> RegisterDynamicAsync(
        Guid campaignId,
        string dynamicFieldName,
        CancellationToken cancellationToken = default)
    {
        // Verify campaign exists
        var campaign = await _campaignRepository.GetByIdAsync(campaignId, cancellationToken);
        if (campaign is null)
            throw new NotFoundException("Campaign", campaignId);

        if (string.IsNullOrWhiteSpace(dynamicFieldName))
            throw new ValidationException(new Dictionary<string, string[]>
            {
                ["dynamicFieldName"] = ["DynamicFieldName must not be empty."]
            });

        // Persist dynamic attachment record (no file is stored)
        var attachment = new CampaignAttachment
        {
            CampaignId = campaignId,
            FileName = $"[dynamic:{dynamicFieldName}]",
            FilePath = string.Empty,
            FileSizeBytes = 0,
            ContentType = string.Empty,
            IsDynamic = true,
            DynamicFieldName = dynamicFieldName
        };

        await _attachmentRepository.AddAsync(attachment, cancellationToken);
        await _unitOfWork.CommitAsync(cancellationToken);

        _logger.LogInformation(
            "Dynamic attachment registered: Campaign={CampaignId}, FieldName={FieldName}",
            campaignId, dynamicFieldName);

        return attachment.Adapt<CampaignAttachmentDto>();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<CampaignAttachmentDto>> GetByCampaignAsync(
        Guid campaignId,
        CancellationToken cancellationToken = default)
    {
        var attachments = await _attachmentRepository.GetByCampaignIdAsync(campaignId, cancellationToken);
        return attachments.Select(a => a.Adapt<CampaignAttachmentDto>()).ToList();
    }

    /// <inheritdoc />
    public async Task DeleteAsync(Guid attachmentId, CancellationToken cancellationToken = default)
    {
        var attachment = await _attachmentRepository.GetByIdAsync(attachmentId, cancellationToken);
        if (attachment is null)
            throw new NotFoundException("Attachment", attachmentId);

        // Fix #8: delete the file first, then the DB record. If the DB delete fails
        // after a successful file delete, the next list/load will show the broken
        // reference and the operator can retry cleanly — far better than the
        // previous inverted order which left orphaned files on the share whenever
        // the file-share call raised (the DB record was already gone by then).
        if (!attachment.IsDynamic && !string.IsNullOrWhiteSpace(attachment.FilePath))
        {
            await _fileUploadService.DeleteAsync(attachment.FilePath);
        }

        _attachmentRepository.Remove(attachment);
        await _unitOfWork.CommitAsync(cancellationToken);

        _logger.LogInformation(
            "Attachment deleted: Id={AttachmentId}, Campaign={CampaignId}, IsDynamic={IsDynamic}",
            attachmentId, attachment.CampaignId, attachment.IsDynamic);
    }

    // ----------------------------------------------------------------
    // Helpers
    // ----------------------------------------------------------------

    private static string GetContentType(string extension) => extension.ToLowerInvariant() switch
    {
        ".pdf"  => "application/pdf",
        ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        ".xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        ".png"  => "image/png",
        ".jpg"  => "image/jpeg",
        ".jpeg" => "image/jpeg",
        _       => "application/octet-stream"
    };
}
