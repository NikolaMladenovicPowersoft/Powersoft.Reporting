using Powersoft.Reporting.Core.Models;

namespace Powersoft.Reporting.Core.Interfaces;

public interface IEmailRecipientRepository
{
    Task<List<EmailRecipient>> GetAllAsync();
    Task<List<EmailRecipient>> SearchAsync(string query);
    Task<EmailRecipient?> AddAsync(string emailAddress, string displayName, string createdBy);
    Task<bool> DeleteAsync(int recipientId);
}
