using Powersoft.Reporting.Core.Models;

namespace Powersoft.Reporting.Core.Interfaces;

/// <summary>
/// Source of industry template packs. POC implementation is code-seeded; a future implementation
/// will read the central psCentral catalog. Kept as an interface so the apply flow does not care
/// where packs come from.
/// </summary>
public interface ITemplatePackCatalog
{
    /// <summary>All active packs, ordered for display.</summary>
    IReadOnlyList<ReportTemplatePack> GetPacks();

    /// <summary>Returns the pack with the given code (case-insensitive), or null if not found.</summary>
    ReportTemplatePack? GetPack(string packCode);
}
