namespace Powersoft.Reporting.Core.Models;

public class EmailTemplate
{
    public int TemplateId { get; set; }
    public string TemplateName { get; set; } = "";
    public string? ReportType { get; set; }
    public string EmailSubject { get; set; } = "";
    public string EmailBodyHtml { get; set; } = "";
    public bool IsDefault { get; set; }
    public bool IsActive { get; set; } = true;
    public string CreatedBy { get; set; } = "";
    public DateTime CreatedDate { get; set; } = DateTime.Now;
}
