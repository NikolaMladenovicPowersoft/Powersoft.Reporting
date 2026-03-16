namespace Powersoft.Reporting.Web.Options;

public sealed class AiAnalyzerOptions
{
    public string Provider { get; set; } = "OpenAI";
    public string ApiKey { get; set; } = "";
    public string Model { get; set; } = "gpt-4o-mini";
    public int MaxOutputTokens { get; set; } = 2048;
    public double Temperature { get; set; } = 0.3;
}
