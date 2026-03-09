namespace Powersoft.Reporting.Core.Models;

public class ReportAnalysis
{
    public string Summary { get; set; } = "";
    public List<string> KeyFindings { get; set; } = new();
    public List<string> Alerts { get; set; } = new();
    public List<string> Recommendations { get; set; } = new();
    public string ModelUsed { get; set; } = "";
    public int InputTokens { get; set; }
    public int OutputTokens { get; set; }
    public double DurationMs { get; set; }
}
