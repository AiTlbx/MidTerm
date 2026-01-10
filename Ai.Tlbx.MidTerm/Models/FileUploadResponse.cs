namespace Ai.Tlbx.MidTerm.Models;

/// <summary>
/// Response payload after uploading a file for terminal drag-and-drop.
/// </summary>
public sealed class FileUploadResponse
{
    public required string Path { get; set; }
}
