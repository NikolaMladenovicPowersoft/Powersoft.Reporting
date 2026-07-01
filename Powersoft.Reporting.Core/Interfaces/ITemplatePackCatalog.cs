using Powersoft.Reporting.Core.Models;

namespace Powersoft.Reporting.Core.Interfaces;

/// <summary>
/// Source of industry template packs. Kept as an interface so the apply flow does not care where
/// packs come from: the code-seeded catalog (tests / fallback) or the central psCentral catalog
/// (authoring at runtime, no redeploy). Read-only — authoring goes through <see cref="ICentralRepository"/>.
/// </summary>
public interface ITemplatePackCatalog
{
    /// <summary>All active packs, ordered for display.</summary>
    Task<IReadOnlyList<ReportTemplatePack>> GetPacksAsync();

    /// <summary>Returns the pack with the given code (case-insensitive), or null if not found.</summary>
    Task<ReportTemplatePack?> GetPackAsync(string packCode);
}
