using Microsoft.Extensions.Options;
using Powersoft.Reporting.Web.Options;

namespace Powersoft.Reporting.Web.Services.AI;

public class ReportAnalyzerFactory
{
    private readonly IServiceProvider _serviceProvider;
    private readonly AiAnalyzerOptions _options;

    public ReportAnalyzerFactory(IServiceProvider serviceProvider, IOptions<AiAnalyzerOptions> options)
    {
        _serviceProvider = serviceProvider;
        _options = options.Value;
    }

    public IReportAnalyzer Create()
    {
        return _options.Provider?.ToLowerInvariant() switch
        {
            "openai" => _serviceProvider.GetRequiredService<OpenAIReportAnalyzer>(),
            _ => _serviceProvider.GetRequiredService<ClaudeReportAnalyzer>()
        };
    }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(_options.ApiKey);
}
