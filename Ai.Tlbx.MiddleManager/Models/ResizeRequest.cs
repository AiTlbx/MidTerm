namespace Ai.Tlbx.MiddleManager.Models;

public sealed class ResizeRequest
{
    public int Cols { get; set; }
    public int Rows { get; set; }
    public string? ViewerId { get; set; }
}
