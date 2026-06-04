namespace Powersoft.Reporting.Core.Models;

public class EmailRecipient
{
    public int RecipientId { get; set; }
    public string EmailAddress { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public bool IsActive { get; set; } = true;
    public DateTime CreatedDate { get; set; }
    public string CreatedBy { get; set; } = "";
}
