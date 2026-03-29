namespace CampaignEngine.Application.Interfaces;

/// <summary>
/// Validates the structural integrity and security of an uploaded DOCX file.
///
/// US-009 — DOCX structural validation (F-203).
///
/// Validation rules enforced (in order):
///   Rule 1: File name must have a .docx extension (case-insensitive). .docm is explicitly rejected.
///   Rule 2: File content must be a valid ZIP archive.
///   Rule 3: The ZIP archive must contain a [Content_Types].xml part at the root.
///   Rule 4: The file must open successfully as a WordprocessingDocument.
///   Rule 5: The ZIP archive must NOT contain a vbaProject.bin part (macro detection).
///
/// All failures throw <see cref="Domain.Exceptions.ValidationException"/> so that
/// the global exception middleware maps them to HTTP 422 Unprocessable Entity.
/// </summary>
public interface IDocxValidationService
{
    /// <summary>
    /// Validates the supplied stream as a DOCX file.
    /// </summary>
    /// <param name="fileName">
    /// The original file name as provided by the client. Used for extension checking.
    /// Must not be <see langword="null"/> or empty.
    /// </param>
    /// <param name="content">
    /// A readable stream containing the raw bytes of the uploaded file.
    /// The stream must be readable and should be positioned at the beginning.
    /// The stream is left open after the call; the caller is responsible for disposal.
    /// </param>
    /// <param name="ct">A token that can be used to cancel the operation.</param>
    /// <exception cref="Domain.Exceptions.ValidationException">
    /// Thrown when any of the structural validation rules are violated.
    /// The exception message identifies the specific rule that failed.
    /// </exception>
    Task ValidateAsync(string fileName, Stream content, CancellationToken ct = default);
}
