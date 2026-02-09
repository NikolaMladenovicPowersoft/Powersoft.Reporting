using Powersoft.Reporting.Core.Models;

namespace Powersoft.Reporting.Core.Interfaces;

public interface ICentralRepository
{
    Task<List<Company>> GetActiveCompaniesAsync();
    Task<List<Database>> GetActiveDatabasesForCompanyAsync(string companyCode);
    Task<Database?> GetDatabaseByCodeAsync(string dbCode);
    Task<bool> TestConnectionAsync();
}
