using System.Text;
using Powersoft.Reporting.Web.Options;

namespace Powersoft.Reporting.Web.Services.AI;

/// <summary>
/// Pre-run cost estimation for AI analysis. Lets us warn/block BEFORE spending tokens,
/// so a 58-page report can't silently burn the monthly budget.
///
/// Token estimate is intentionally conservative (rounds up): OpenAI averages ~4 chars
/// per token for English/CSV, but financial CSV with many short numeric cells tokenizes
/// denser, so we use a slightly lower divisor to avoid under-estimating.
/// </summary>
public static class AiCostEstimator
{
    // Chars-per-token divisor. Lower = more pessimistic (safer) estimate.
    private const double CharsPerToken = 3.5;

    // Fixed system+user prompt scaffolding overhead (tokens), independent of data size.
    private const int PromptOverheadTokens = 450;

    public static int EstimateInputTokens(string text)
    {
        if (string.IsNullOrEmpty(text)) return PromptOverheadTokens;
        var chars = Encoding.UTF8.GetByteCount(text);
        return PromptOverheadTokens + (int)Math.Ceiling(chars / CharsPerToken);
    }

    /// <summary>Estimated output tokens — we assume the model uses its full output budget.</summary>
    public static int EstimateOutputTokens(AiAnalyzerOptions options) => options.MaxOutputTokens;

    public static decimal EstimateCost(string text, AiAnalyzerOptions options)
    {
        var inTok = EstimateInputTokens(text);
        var outTok = EstimateOutputTokens(options);
        return ComputeCost(inTok, outTok, options);
    }

    /// <summary>Actual cost from measured token counts (for logging after the call).</summary>
    public static decimal ComputeCost(int inputTokens, int outputTokens, AiAnalyzerOptions options)
    {
        var input = inputTokens / 1_000_000m * options.InputCostPer1MTokens;
        var output = outputTokens / 1_000_000m * options.OutputCostPer1MTokens;
        return Math.Round(input + output, 6);
    }
}
