namespace Powersoft.Reporting.Core.Models;

public class ScheduleLog
{
    public int LogId { get; set; }
    public int ScheduleId { get; set; }
    public DateTime RunDate { get; set; } = DateTime.UtcNow;
    public string Status { get; set; } = "Success";
    public int? RowsGenerated { get; set; }
    public long? FileSizeBytes { get; set; }
    public string? ErrorMessage { get; set; }
    public int? DurationMs { get; set; }
    public int? InputTokens { get; set; }
    public int? OutputTokens { get; set; }
    public decimal? EstimatedCost { get; set; }
}

public class ScheduleLogEntry
{
    public int LogId { get; set; }
    public int ScheduleId { get; set; }
    public string ScheduleName { get; set; } = "";
    public string ReportType { get; set; } = "";
    public DateTime RunDate { get; set; }
    public string Status { get; set; } = "";
    public int? RowsGenerated { get; set; }
    public long? FileSizeBytes { get; set; }
    public string? ErrorMessage { get; set; }
    public int? DurationMs { get; set; }
    public int? InputTokens { get; set; }
    public int? OutputTokens { get; set; }
    public decimal? EstimatedCost { get; set; }
}

public static class ScheduleLogStatus
{
    public const string Success = "Success";
    public const string Failed = "Failed";
    public const string Skipped = "Skipped";
}
