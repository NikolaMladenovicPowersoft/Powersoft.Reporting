namespace Powersoft.Reporting.Web.Options;

public sealed class AiAnalyzerOptions
{
    public string Provider { get; set; } = "OpenAI";
    public string ApiKey { get; set; } = "";
    public string Model { get; set; } = "gpt-4o-mini";
    public int MaxOutputTokens { get; set; } = 2048;
    public double Temperature { get; set; } = 0.3;

    /// <summary>USD cost per 1,000,000 input (prompt) tokens. Default = gpt-4o-mini.</summary>
    public decimal InputCostPer1MTokens { get; set; } = 0.15m;

    /// <summary>USD cost per 1,000,000 output (completion) tokens. Default = gpt-4o-mini.</summary>
    public decimal OutputCostPer1MTokens { get; set; } = 0.60m;
}
