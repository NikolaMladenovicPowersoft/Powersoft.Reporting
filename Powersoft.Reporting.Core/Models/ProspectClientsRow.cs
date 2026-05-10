namespace Powersoft.Reporting.Core.Models;

public class ProspectClientsRow
{
    public string LeadNo { get; set; } = "";
    public string Level1Code { get; set; } = "";
    public string Level1Descr { get; set; } = "";
    public string Level2Code { get; set; } = "";
    public string Level2Descr { get; set; } = "";
    public bool IsCompany { get; set; }
    public string CompanyName { get; set; } = "";
    public string ContactPerson { get; set; } = "";
    public string ShortName { get; set; } = "";
    public string StatusName { get; set; } = "";
    public string StatusColor { get; set; } = "";
    public string PriorityName { get; set; } = "";
    public string PositionName { get; set; } = "";
    public DateTime? RegistrationDate { get; set; }
    public DateTime? CreationDateTime { get; set; }
    public DateTime? LastModification { get; set; }
    public DateTime? NextCommunicationDate { get; set; }
    public string Tel1 { get; set; } = "";
    public string Tel2 { get; set; } = "";
    public string Mobile { get; set; } = "";
    public string Email { get; set; } = "";
    public string WebSite { get; set; } = "";
    public string Address { get; set; } = "";
    public string PostalCode { get; set; } = "";
    public string Town { get; set; } = "";
    public string FollowedBy { get; set; } = "";
    public string RecommendedBy { get; set; } = "";
    public string LinkedCustomer { get; set; } = "";
    public string Category1 { get; set; } = "";
    public string Category2 { get; set; } = "";
    public string Category3 { get; set; } = "";
    public string Notes { get; set; } = "";
    public string CreatedBy { get; set; } = "";
    public string LastModifiedBy { get; set; } = "";
    public string Source { get; set; } = "Active";

    // Offer metrics
    public int OfferCount { get; set; }
    public decimal TotalOfferValue { get; set; }

    // Communication metrics
    public int EmailsSent { get; set; }
    public int SmsSent { get; set; }
    public int TotalCommunications => EmailsSent + SmsSent;
}
