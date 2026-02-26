using Powersoft.Reporting.Core.Constants;

namespace Powersoft.Reporting.Core.Models;

/// <summary>
/// Typed wrapper around tbl_Ini* key-value pairs for database-level settings.
/// Stored with moduleCode = RENGINEAI, headerCode = DBSETTINGS, userCode = ALL.
/// </summary>
public class DatabaseSettings
{
    public int MaxSchedulesPerReport { get; set; } = ModuleConstants.ScheduleLimitDefault;
    public string DefaultExportFormat { get; set; } = ModuleConstants.DefaultExportFormatValue;
    public bool SchedulerEnabled { get; set; } = true;
    public int RetentionDays { get; set; } = ModuleConstants.DefaultRetentionDays;

    public static DatabaseSettings FromDictionary(Dictionary<string, string> ini)
    {
        var settings = new DatabaseSettings();

        if (ini.TryGetValue(ModuleConstants.SettingMaxSchedules, out var maxSch)
            && int.TryParse(maxSch, out var maxSchVal) && maxSchVal > 0)
            settings.MaxSchedulesPerReport = maxSchVal;

        if (ini.TryGetValue(ModuleConstants.SettingDefaultExportFormat, out var fmt)
            && !string.IsNullOrWhiteSpace(fmt))
            settings.DefaultExportFormat = fmt;

        if (ini.TryGetValue(ModuleConstants.SettingSchedulerEnabled, out var schEnabled))
            settings.SchedulerEnabled = !string.Equals(schEnabled, "0", StringComparison.Ordinal)
                                     && !string.Equals(schEnabled, "false", StringComparison.OrdinalIgnoreCase);

        if (ini.TryGetValue(ModuleConstants.SettingRetentionDays, out var ret)
            && int.TryParse(ret, out var retVal) && retVal > 0)
            settings.RetentionDays = retVal;

        return settings;
    }

    public Dictionary<string, string> ToDictionary()
    {
        return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [ModuleConstants.SettingMaxSchedules] = MaxSchedulesPerReport.ToString(),
            [ModuleConstants.SettingDefaultExportFormat] = DefaultExportFormat,
            [ModuleConstants.SettingSchedulerEnabled] = SchedulerEnabled ? "1" : "0",
            [ModuleConstants.SettingRetentionDays] = RetentionDays.ToString()
        };
    }
}
