namespace Powersoft.Reporting.Web.Options;

public sealed class AiAnalyzerOptions
{
    public string Provider { get; set; } = "Claude";
    public string ApiKey { get; set; } = "";
    public string Model { get; set; } = "claude-opus-4-6-20250204";
    public int MaxOutputTokens { get; set; } = 2048;
    public double Temperature { get; set; } = 0.3;
}
