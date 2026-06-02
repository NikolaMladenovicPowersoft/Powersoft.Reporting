namespace Powersoft.Reporting.Core.Models;

public class FilterPreset
{
    public int PresetId { get; set; }
    public string PresetName { get; set; } = "";
    public string? ReportType { get; set; }
    public string FilterJson { get; set; } = "{}";
    public string CreatedBy { get; set; } = "";
    public DateTime CreatedDate { get; set; }
    public DateTime? ModifiedDate { get; set; }
    public bool IsShared { get; set; }
}
