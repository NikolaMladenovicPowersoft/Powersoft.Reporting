using System.ComponentModel.DataAnnotations;

namespace Powersoft.Reporting.Web.ViewModels;

public class DatabaseSettingsViewModel
{
    public string? ConnectedDatabase { get; set; }
    public string? ConnectedDatabaseCode { get; set; }

    [Range(1, 100, ErrorMessage = "Must be between 1 and 100")]
    [Display(Name = "Max Schedules Per Report")]
    public int MaxSchedulesPerReport { get; set; } = 5;

    [Display(Name = "Default Export Format")]
    public string DefaultExportFormat { get; set; } = "Excel";

    [Display(Name = "Scheduler Enabled")]
    public bool SchedulerEnabled { get; set; } = true;

    [Range(1, 365, ErrorMessage = "Must be between 1 and 365 days")]
    [Display(Name = "Report Retention (days)")]
    public int RetentionDays { get; set; } = 7;

    [Range(1000, 100000000, ErrorMessage = "Must be between 1,000 and 100,000,000")]
    [Display(Name = "Monthly AI Token Budget")]
    public int MonthlyTokenLimit { get; set; } = 500000;

    public int CurrentMonthUsed { get; set; }

    public bool IsSystemAdmin { get; set; }
    public bool CanEdit { get; set; }
    public string? SuccessMessage { get; set; }
}
