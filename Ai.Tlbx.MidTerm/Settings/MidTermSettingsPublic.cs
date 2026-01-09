using Ai.Tlbx.MidTerm.Common.Logging;
using Ai.Tlbx.MidTerm.Common.Shells;

namespace Ai.Tlbx.MidTerm.Settings;

public sealed class MidTermSettingsPublic
{
    // Session Defaults
    public ShellType DefaultShell { get; set; }
    public int DefaultCols { get; set; }
    public int DefaultRows { get; set; }
    public string DefaultWorkingDirectory { get; set; } = "";

    // Terminal Appearance
    public int FontSize { get; set; }
    public string FontFamily { get; set; } = "";
    public CursorStyleSetting CursorStyle { get; set; }
    public bool CursorBlink { get; set; }
    public ThemeSetting Theme { get; set; }
    public double MinimumContrastRatio { get; set; }
    public bool SmoothScrolling { get; set; }
    public bool UseWebGL { get; set; }

    // Terminal Behavior
    public int ScrollbackLines { get; set; }
    public BellStyleSetting BellStyle { get; set; }
    public bool CopyOnSelect { get; set; }
    public bool RightClickPaste { get; set; }
    public ClipboardShortcutsSetting ClipboardShortcuts { get; set; }

    // Security - User to spawn terminals as (when running as service)
    public string? RunAsUser { get; set; }
    public string? RunAsUserSid { get; set; }
    public int? RunAsUid { get; set; }
    public int? RunAsGid { get; set; }

    // Authentication (public fields only - no PasswordHash, SessionSecret)
    public bool AuthenticationEnabled { get; set; }

    // HTTPS (always enabled - no CertificatePassword exposed)
    public string? CertificatePath { get; set; }

    // Diagnostics
    public LogSeverity LogLevel { get; set; }

    public static MidTermSettingsPublic FromSettings(MidTermSettings settings)
    {
        return new MidTermSettingsPublic
        {
            DefaultShell = settings.DefaultShell,
            DefaultCols = settings.DefaultCols,
            DefaultRows = settings.DefaultRows,
            DefaultWorkingDirectory = settings.DefaultWorkingDirectory,
            FontSize = settings.FontSize,
            FontFamily = settings.FontFamily,
            CursorStyle = settings.CursorStyle,
            CursorBlink = settings.CursorBlink,
            Theme = settings.Theme,
            MinimumContrastRatio = settings.MinimumContrastRatio,
            SmoothScrolling = settings.SmoothScrolling,
            UseWebGL = settings.UseWebGL,
            ScrollbackLines = settings.ScrollbackLines,
            BellStyle = settings.BellStyle,
            CopyOnSelect = settings.CopyOnSelect,
            RightClickPaste = settings.RightClickPaste,
            ClipboardShortcuts = settings.ClipboardShortcuts,
            RunAsUser = settings.RunAsUser,
            RunAsUserSid = settings.RunAsUserSid,
            RunAsUid = settings.RunAsUid,
            RunAsGid = settings.RunAsGid,
            AuthenticationEnabled = settings.AuthenticationEnabled,
            CertificatePath = settings.CertificatePath,
            LogLevel = settings.LogLevel
        };
    }

    public void ApplyTo(MidTermSettings settings)
    {
        settings.DefaultShell = DefaultShell;
        settings.DefaultCols = DefaultCols;
        settings.DefaultRows = DefaultRows;
        settings.DefaultWorkingDirectory = DefaultWorkingDirectory;
        settings.FontSize = FontSize;
        settings.FontFamily = FontFamily;
        settings.CursorStyle = CursorStyle;
        settings.CursorBlink = CursorBlink;
        settings.Theme = Theme;
        settings.MinimumContrastRatio = MinimumContrastRatio;
        settings.SmoothScrolling = SmoothScrolling;
        settings.UseWebGL = UseWebGL;
        settings.ScrollbackLines = ScrollbackLines;
        settings.BellStyle = BellStyle;
        settings.CopyOnSelect = CopyOnSelect;
        settings.RightClickPaste = RightClickPaste;
        settings.ClipboardShortcuts = ClipboardShortcuts;
        settings.RunAsUser = RunAsUser;
        settings.RunAsUserSid = RunAsUserSid;
        settings.RunAsUid = RunAsUid;
        settings.RunAsGid = RunAsGid;
        settings.AuthenticationEnabled = AuthenticationEnabled;
        settings.CertificatePath = CertificatePath;
        settings.LogLevel = LogLevel;
    }
}
