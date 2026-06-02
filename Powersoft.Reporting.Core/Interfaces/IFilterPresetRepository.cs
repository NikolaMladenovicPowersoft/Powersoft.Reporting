using Powersoft.Reporting.Core.Models;

namespace Powersoft.Reporting.Core.Interfaces;

public interface IFilterPresetRepository
{
    Task<List<FilterPreset>> GetPresetsAsync(string connectionString, string userCode, string? reportType);
    Task<FilterPreset?> GetPresetByIdAsync(string connectionString, int presetId);
    Task<int> SavePresetAsync(string connectionString, FilterPreset preset);
    Task<bool> DeletePresetAsync(string connectionString, int presetId, string userCode);
}
