using Powersoft.Reporting.Core.Models;

namespace Powersoft.Reporting.Core.Interfaces;

public interface IPurchasesSalesRepository
{
    Task<PagedResult<PurchasesSalesRow>> GetPurchasesSalesDataAsync(PurchasesSalesFilter filter);
    Task<List<TransactionDetailRow>> GetTransactionDetailsAsync(
        string itemCode, string transactionType, DateTime dateFrom, DateTime dateTo, List<string>? storeCodes = null);
    Task<DocumentDetailResult?> GetDocumentDetailAsync(string docType, string documentNumber);
    Task<bool> TestConnectionAsync();
}
