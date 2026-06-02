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

    // Extra fields from tbl_RelLeadAttributes
    public int? IntVal1 { get; set; }
    public int? IntVal2 { get; set; }
    public int? IntVal3 { get; set; }
    public int? IntVal4 { get; set; }
    public int? IntVal5 { get; set; }
    public int? IntVal6 { get; set; }
    public int? IntVal7 { get; set; }
    public int? IntVal8 { get; set; }
    public int? IntVal9 { get; set; }
    public int? IntVal10 { get; set; }
    public decimal? NumVal1 { get; set; }
    public decimal? NumVal2 { get; set; }
    public decimal? NumVal3 { get; set; }
    public decimal? NumVal4 { get; set; }
    public decimal? NumVal5 { get; set; }
    public decimal? NumVal6 { get; set; }
    public decimal? NumVal7 { get; set; }
    public decimal? NumVal8 { get; set; }
    public decimal? NumVal9 { get; set; }
    public decimal? NumVal10 { get; set; }
    public bool? BoolVal1 { get; set; }
    public bool? BoolVal2 { get; set; }
    public bool? BoolVal3 { get; set; }
    public bool? BoolVal4 { get; set; }
    public bool? BoolVal5 { get; set; }
    public bool? BoolVal6 { get; set; }
    public bool? BoolVal7 { get; set; }
    public bool? BoolVal8 { get; set; }
    public bool? BoolVal9 { get; set; }
    public bool? BoolVal10 { get; set; }
    public DateTime? DateVal1 { get; set; }
    public DateTime? DateVal2 { get; set; }
    public DateTime? DateVal3 { get; set; }
    public DateTime? DateVal4 { get; set; }
    public DateTime? DateVal5 { get; set; }
    public DateTime? DateVal6 { get; set; }
    public DateTime? DateVal7 { get; set; }
    public DateTime? DateVal8 { get; set; }
    public DateTime? DateVal9 { get; set; }
    public DateTime? DateVal10 { get; set; }
    public DateTime? DateVal11 { get; set; }
    public DateTime? DateVal12 { get; set; }
    public DateTime? DateVal13 { get; set; }
    public DateTime? DateVal14 { get; set; }
    public DateTime? DateVal15 { get; set; }
    public string TextVal1 { get; set; } = "";
    public string TextVal2 { get; set; } = "";
    public string TextVal3 { get; set; } = "";
    public string TextVal4 { get; set; } = "";
    public string TextVal5 { get; set; } = "";
    public string TextVal6 { get; set; } = "";
    public string TextVal7 { get; set; } = "";
    public string TextVal8 { get; set; } = "";
    public string TextVal9 { get; set; } = "";
    public string TextVal10 { get; set; } = "";
}
