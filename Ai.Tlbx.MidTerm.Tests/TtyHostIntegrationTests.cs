using System.Diagnostics;
using Xunit;
using Xunit.Abstractions;

namespace Ai.Tlbx.MidTerm.Tests;

public class TtyHostIntegrationTests
{
    private readonly ITestOutputHelper _output;

    private static readonly string TtyHostPath = FindTtyHostExe();

    public TtyHostIntegrationTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void TtyHostExe_Exists()
    {
        _output.WriteLine($"Looking for mthost.exe at: {TtyHostPath}");
        Assert.True(File.Exists(TtyHostPath), $"mthost.exe not found at {TtyHostPath}");
    }

    [Fact]
    public void ZZZ_NoOrphanMthostProcesses_AfterAllTests()
    {
        var orphans = TestCleanupHelper.GetOrphanTestMthostProcesses();

        if (orphans.Count > 0)
        {
            var paths = orphans.Select(p =>
            {
                try { return p.MainModule?.FileName ?? "unknown"; }
                catch { return "access denied"; }
            });

            TestCleanupHelper.KillOrphanTestProcesses();

            Assert.Fail($"Found {orphans.Count} orphan mthost processes from test builds: {string.Join(", ", paths)}");
        }
    }

    private static string FindTtyHostExe()
    {
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "Ai.Tlbx.MidTerm.TtyHost", "bin", "Debug", "net10.0", "win-x64", "mthost.exe"),
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "Ai.Tlbx.MidTerm.TtyHost", "bin", "Release", "net10.0", "win-x64", "mthost.exe"),
            @"C:\Program Files\MidTerm\mthost.exe",
        };

        foreach (var path in candidates)
        {
            var fullPath = Path.GetFullPath(path);
            if (File.Exists(fullPath))
            {
                return fullPath;
            }
        }

        return Path.GetFullPath(candidates[0]);
    }
}
