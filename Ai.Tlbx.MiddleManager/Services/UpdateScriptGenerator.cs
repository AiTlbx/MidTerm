namespace Ai.Tlbx.MiddleManager.Services;

public static class UpdateScriptGenerator
{
    private const string WebServiceName = "MiddleManager";
    private const string HostServiceName = "MiddleManagerHost";
    private const string LaunchdWebLabel = "com.aitlbx.middlemanager";
    private const string LaunchdHostLabel = "com.aitlbx.middlemanager-host";
    private const string SystemdWebService = "middlemanager";
    private const string SystemdHostService = "middlemanager-host";

    public static string GenerateUpdateScript(string extractedDir, string currentBinaryPath)
    {
        if (OperatingSystem.IsWindows())
        {
            return GenerateWindowsScript(extractedDir, currentBinaryPath);
        }

        return GenerateUnixScript(extractedDir, currentBinaryPath);
    }

    private static string GenerateWindowsScript(string extractedDir, string currentBinaryPath)
    {
        var installDir = Path.GetDirectoryName(currentBinaryPath) ?? currentBinaryPath;
        var newWebBinaryPath = Path.Combine(extractedDir, "mm.exe");
        var newHostBinaryPath = Path.Combine(extractedDir, "mm-host.exe");
        var newConHostBinaryPath = Path.Combine(extractedDir, "mm-con-host.exe");
        var newVersionJsonPath = Path.Combine(extractedDir, "version.json");
        var currentHostBinaryPath = Path.Combine(installDir, "mm-host.exe");
        var currentConHostBinaryPath = Path.Combine(installDir, "mm-con-host.exe");
        var currentVersionJsonPath = Path.Combine(installDir, "version.json");
        var scriptPath = Path.Combine(Path.GetTempPath(), $"mm-update-{Guid.NewGuid():N}.ps1");

        var script = $@"
# MiddleManager Update Script (v3.0+ with ConHost)
$ErrorActionPreference = 'SilentlyContinue'

# Wait for main process to exit
Start-Sleep -Seconds 2

# Stop web service first
$webService = Get-Service -Name '{WebServiceName}' -ErrorAction SilentlyContinue
if ($webService) {{
    Stop-Service -Name '{WebServiceName}' -Force
    Start-Sleep -Seconds 2
}}

# Stop host service if running
$hostService = Get-Service -Name '{HostServiceName}' -ErrorAction SilentlyContinue
if ($hostService) {{
    Stop-Service -Name '{HostServiceName}' -Force
    Start-Sleep -Seconds 2
}}

# Kill any remaining mm.exe and mm-con-host processes (orphaned sessions)
Get-Process -Name 'mm' -ErrorAction SilentlyContinue | Stop-Process -Force
Get-Process -Name 'mm-con-host' -ErrorAction SilentlyContinue | Stop-Process -Force
Get-Process -Name 'mm-host' -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Seconds 1

# Backup current binaries
$webBinary = '{currentBinaryPath}'
$hostBinary = '{currentHostBinaryPath}'
$conHostBinary = '{currentConHostBinaryPath}'

if (Test-Path $webBinary) {{
    Copy-Item $webBinary ($webBinary + '.bak') -Force
}}
if (Test-Path $hostBinary) {{
    Copy-Item $hostBinary ($hostBinary + '.bak') -Force
}}
if (Test-Path $conHostBinary) {{
    Copy-Item $conHostBinary ($conHostBinary + '.bak') -Force
}}

# Copy new binaries
$newWebBinary = '{newWebBinaryPath}'
$newHostBinary = '{newHostBinaryPath}'
$newConHostBinary = '{newConHostBinaryPath}'
$newVersionJson = '{newVersionJsonPath}'
$currentVersionJson = '{currentVersionJsonPath}'

Copy-Item $newWebBinary $webBinary -Force
if (Test-Path $newHostBinary) {{
    Copy-Item $newHostBinary $hostBinary -Force
}}
if (Test-Path $newConHostBinary) {{
    Copy-Item $newConHostBinary $conHostBinary -Force
}}
if (Test-Path $newVersionJson) {{
    Copy-Item $newVersionJson $currentVersionJson -Force
}}

# Start host service first (if it exists)
if ($hostService) {{
    Start-Service -Name '{HostServiceName}'
    Start-Sleep -Seconds 1
}}

# Start web service
if ($webService) {{
    Start-Service -Name '{WebServiceName}'
}} else {{
    # Start the binary directly (it will spawn mm-host if needed)
    Start-Process -FilePath $webBinary -WindowStyle Hidden
}}

# Cleanup
Start-Sleep -Seconds 2
Remove-Item ($webBinary + '.bak') -Force -ErrorAction SilentlyContinue
Remove-Item ($hostBinary + '.bak') -Force -ErrorAction SilentlyContinue
Remove-Item ($conHostBinary + '.bak') -Force -ErrorAction SilentlyContinue
Remove-Item -Path '{extractedDir}' -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item $MyInvocation.MyCommand.Path -Force -ErrorAction SilentlyContinue
";

        File.WriteAllText(scriptPath, script);
        return scriptPath;
    }

    private static string GenerateUnixScript(string extractedDir, string currentBinaryPath)
    {
        var installDir = Path.GetDirectoryName(currentBinaryPath) ?? "/usr/local/bin";
        var newWebBinaryPath = Path.Combine(extractedDir, "mm");
        var newHostBinaryPath = Path.Combine(extractedDir, "mm-host");
        var newVersionJsonPath = Path.Combine(extractedDir, "version.json");
        var currentHostBinaryPath = Path.Combine(installDir, "mm-host");
        var currentVersionJsonPath = Path.Combine(installDir, "version.json");
        var scriptPath = Path.Combine(Path.GetTempPath(), $"mm-update-{Guid.NewGuid():N}.sh");

        var isMacOs = OperatingSystem.IsMacOS();

        var stopWebService = isMacOs
            ? $"launchctl unload /Library/LaunchDaemons/{LaunchdWebLabel}.plist 2>/dev/null || true"
            : $"systemctl stop {SystemdWebService} 2>/dev/null || true";
        var stopHostService = isMacOs
            ? $"launchctl unload /Library/LaunchDaemons/{LaunchdHostLabel}.plist 2>/dev/null || true"
            : $"systemctl stop {SystemdHostService} 2>/dev/null || true";
        var startHostService = isMacOs
            ? $"launchctl load /Library/LaunchDaemons/{LaunchdHostLabel}.plist 2>/dev/null || true"
            : $"systemctl start {SystemdHostService} 2>/dev/null || true";
        var startWebService = isMacOs
            ? $"launchctl load /Library/LaunchDaemons/{LaunchdWebLabel}.plist 2>/dev/null || true"
            : $"systemctl start {SystemdWebService} 2>/dev/null || true";

        var script = $@"#!/bin/bash
# MiddleManager Update Script (v2.0+ with sidecar)

# Wait for main process to exit
sleep 2

# Stop web service first
{stopWebService}

# Stop host service
{stopHostService}

# Kill any remaining mm and mm-con-host processes (orphaned sessions)
pkill -f '/mm$' 2>/dev/null || true
pkill -f 'mm-con-host' 2>/dev/null || true
sleep 1

# Backup current binaries
WEB_BINARY='{currentBinaryPath}'
HOST_BINARY='{currentHostBinaryPath}'

if [ -f ""$WEB_BINARY"" ]; then
    cp ""$WEB_BINARY"" ""$WEB_BINARY.bak""
fi
if [ -f ""$HOST_BINARY"" ]; then
    cp ""$HOST_BINARY"" ""$HOST_BINARY.bak""
fi

# Copy new binaries
NEW_WEB_BINARY='{newWebBinaryPath}'
NEW_HOST_BINARY='{newHostBinaryPath}'
NEW_VERSION_JSON='{newVersionJsonPath}'
CURRENT_VERSION_JSON='{currentVersionJsonPath}'

cp ""$NEW_WEB_BINARY"" ""$WEB_BINARY""
chmod +x ""$WEB_BINARY""

if [ -f ""$NEW_HOST_BINARY"" ]; then
    cp ""$NEW_HOST_BINARY"" ""$HOST_BINARY""
    chmod +x ""$HOST_BINARY""
fi

if [ -f ""$NEW_VERSION_JSON"" ]; then
    cp ""$NEW_VERSION_JSON"" ""$CURRENT_VERSION_JSON""
fi

# Start host service first
{startHostService}
sleep 1

# Start web service
{startWebService}

# If services didn't start (not installed as service), start web directly (it spawns host)
if ! pgrep -f ""$WEB_BINARY"" > /dev/null; then
    nohup ""$WEB_BINARY"" > /dev/null 2>&1 &
fi

# Cleanup
sleep 2
rm -f ""$WEB_BINARY.bak""
rm -f ""$HOST_BINARY.bak""
rm -rf '{extractedDir}'
rm -f ""$0""
";

        File.WriteAllText(scriptPath, script);

        if (!OperatingSystem.IsWindows())
        {
            try
            {
                File.SetUnixFileMode(scriptPath,
                    UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                    UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
                    UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
            }
            catch
            {
            }
        }

        return scriptPath;
    }

    public static void ExecuteUpdateScript(string scriptPath)
    {
        if (OperatingSystem.IsWindows())
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "pwsh",
                Arguments = $"-ExecutionPolicy Bypass -File \"{scriptPath}\"",
                UseShellExecute = true,
                CreateNoWindow = true,
                WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden
            });
        }
        else
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "/bin/bash",
                Arguments = $"\"{scriptPath}\"",
                UseShellExecute = false,
                CreateNoWindow = true
            });
        }
    }
}
