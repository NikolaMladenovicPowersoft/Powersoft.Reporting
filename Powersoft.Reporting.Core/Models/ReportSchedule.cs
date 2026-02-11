namespace Powersoft.Reporting.Core.Models;

public class ReportSchedule
{
    public int ScheduleId { get; set; }
    public string ReportType { get; set; } = string.Empty;
    public string ScheduleName { get; set; } = string.Empty;
    public string CreatedBy { get; set; } = string.Empty;
    public DateTime CreatedDate { get; set; }
    public bool IsActive { get; set; } = true;
    
    public string RecurrenceType { get; set; } = "Daily";
    public int? RecurrenceDay { get; set; }
    public TimeSpan ScheduleTime { get; set; } = new TimeSpan(8, 0, 0);
    public DateTime? NextRunDate { get; set; }
    public DateTime? LastRunDate { get; set; }
    
    public string? ParametersJson { get; set; }
    
    public string ExportFormat { get; set; } = "Excel";
    public string Recipients { get; set; } = string.Empty;
    public string? EmailSubject { get; set; }
}
