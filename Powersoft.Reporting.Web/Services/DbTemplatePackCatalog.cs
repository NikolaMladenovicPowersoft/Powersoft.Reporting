using Powersoft.Reporting.Core.Interfaces;
using Powersoft.Reporting.Core.Models;

namespace Powersoft.Reporting.Web.Services;

/// <summary>
/// Reads industry template packs from the central psCentral catalog (authored at runtime, no redeploy).
/// Fail-soft: if the central tables are missing or unreachable, returns no packs rather than throwing,
/// so the reports dashboard still loads. Startup ensures/seeds the schema (see Program.cs).
/// </summary>
public class DbTemplatePackCatalog : ITemplatePackCatalog
{
    private readonly ICentralRepository _central;
    private readonly ILogger<DbTemplatePackCatalog> _logger;

    public DbTemplatePackCatalog(ICentralRepository central, ILogger<DbTemplatePackCatalog> logger)
    {
        _central = central;
        _logger = logger;
    }

    public async Task<IReadOnlyList<ReportTemplatePack>> GetPacksAsync()
    {
        try
        {
            return await _central.GetTemplatePacksAsync();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not read central template packs; returning none");
            return Array.Empty<ReportTemplatePack>();
        }
    }

    public async Task<ReportTemplatePack?> GetPackAsync(string packCode)
    {
        try
        {
            return await _central.GetTemplatePackAsync(packCode);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not read central template pack {Pack}", packCode);
            return null;
        }
    }
}
