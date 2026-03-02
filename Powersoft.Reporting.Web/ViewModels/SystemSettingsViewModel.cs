using System.ComponentModel.DataAnnotations;

namespace Powersoft.Reporting.Web.ViewModels;

public class SystemSettingsViewModel
{
    [Display(Name = "Scheduler Master Switch")]
    public bool SchedulerMasterEnabled { get; set; } = true;

    [Display(Name = "Max Databases Per Run")]
    [Range(1, 500)]
    public int MaxDatabasesPerRun { get; set; } = 50;

    [Display(Name = "Global Max Schedules Per Report")]
    [Range(1, 100)]
    public int GlobalMaxSchedulesPerReport { get; set; } = 20;

    [Display(Name = "Default Retention Days")]
    [Range(1, 365)]
    public int DefaultRetentionDays { get; set; } = 7;

    [Display(Name = "SMTP From Email")]
    public string? DefaultSmtpFromEmail { get; set; } = "";

    [Display(Name = "SMTP From Name")]
    public string? DefaultSmtpFromName { get; set; } = "Powersoft Reports";

    public string? SuccessMessage { get; set; }
    public string? ErrorMessage { get; set; }
}
