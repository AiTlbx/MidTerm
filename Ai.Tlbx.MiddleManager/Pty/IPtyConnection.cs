namespace Ai.Tlbx.MiddleManager.Pty;

public interface IPtyConnection : IDisposable
{
    Stream WriterStream { get; }
    Stream ReaderStream { get; }
    int Pid { get; }
    bool IsRunning { get; }
    int? ExitCode { get; }
    void Resize(int cols, int rows);
    void Kill();
    bool WaitForExit(int milliseconds);
}
