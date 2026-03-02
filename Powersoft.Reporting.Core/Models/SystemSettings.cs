namespace Powersoft.Reporting.Core.Models;

public class SystemSettings
{
    public bool SchedulerMasterEnabled { get; set; } = true;
    public int MaxDatabasesPerRun { get; set; } = 50;
    public int GlobalMaxSchedulesPerReport { get; set; } = 20;
    public int DefaultRetentionDays { get; set; } = 7;
    public string DefaultSmtpFromEmail { get; set; } = "";
    public string DefaultSmtpFromName { get; set; } = "Powersoft Reports";

    private const string Prefix = "RE_";

    public static SystemSettings FromDictionary(Dictionary<string, string> dict)
    {
        var s = new SystemSettings();
        if (dict.TryGetValue(Prefix + "SchedulerMasterEnabled", out var v1))
            s.SchedulerMasterEnabled = v1 == "1" || v1.Equals("true", StringComparison.OrdinalIgnoreCase);
        if (dict.TryGetValue(Prefix + "MaxDatabasesPerRun", out var v2) && int.TryParse(v2, out var n2))
            s.MaxDatabasesPerRun = n2;
        if (dict.TryGetValue(Prefix + "GlobalMaxSchedulesPerReport", out var v3) && int.TryParse(v3, out var n3))
            s.GlobalMaxSchedulesPerReport = n3;
        if (dict.TryGetValue(Prefix + "DefaultRetentionDays", out var v4) && int.TryParse(v4, out var n4))
            s.DefaultRetentionDays = n4;
        if (dict.TryGetValue(Prefix + "SmtpFromEmail", out var v5))
            s.DefaultSmtpFromEmail = v5;
        if (dict.TryGetValue(Prefix + "SmtpFromName", out var v6))
            s.DefaultSmtpFromName = v6;
        return s;
    }

    public Dictionary<string, (string desc, string dataType, string value)> ToSettingsList()
    {
        return new Dictionary<string, (string, string, string)>
        {
            [Prefix + "SchedulerMasterEnabled"] = ("Report Engine — Master scheduler enabled", "bit", SchedulerMasterEnabled ? "1" : "0"),
            [Prefix + "MaxDatabasesPerRun"] = ("Report Engine — Max databases processed per scheduler run", "int", MaxDatabasesPerRun.ToString()),
            [Prefix + "GlobalMaxSchedulesPerReport"] = ("Report Engine — Global cap on active schedules per report", "int", GlobalMaxSchedulesPerReport.ToString()),
            [Prefix + "DefaultRetentionDays"] = ("Report Engine — Default file retention days", "int", DefaultRetentionDays.ToString()),
            [Prefix + "SmtpFromEmail"] = ("Report Engine — Sender email address", "nvarchar", DefaultSmtpFromEmail),
            [Prefix + "SmtpFromName"] = ("Report Engine — Sender display name", "nvarchar", DefaultSmtpFromName),
        };
    }
}
