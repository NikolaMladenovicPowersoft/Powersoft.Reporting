namespace Powersoft.Reporting.Core.Models;

public class AiPromptTemplate
{
    public int TemplateId { get; set; }
    public string TemplateName { get; set; } = "";
    /// <summary>Null = applies to all reports. Otherwise "AverageBasket", "PurchasesSales", etc.</summary>
    public string? ReportType { get; set; }
    /// <summary>The full system prompt text sent to the AI model.</summary>
    public string SystemPrompt { get; set; } = "";
    public bool IsDefault { get; set; }
    public bool IsActive { get; set; } = true;
    public string CreatedBy { get; set; } = "";
    public DateTime CreatedDate { get; set; }
    public DateTime? ModifiedDate { get; set; }
    public string? ModifiedBy { get; set; }
}
