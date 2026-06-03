namespace Powersoft.Reporting.Core.Models;

public class ProspectClientsFilter
{
    public DateTime DateFrom { get; set; } = new DateTime(DateTime.Today.Year, 1, 1);
    public DateTime DateTo { get; set; } = DateTime.Today;
    public string DateField { get; set; } = "RegistrationDate";
    public string StatusFilter { get; set; } = "All";
    public string PriorityFilter { get; set; } = "All";
    public string PrimaryGroup { get; set; } = "NONE";
    public string SecondaryGroup { get; set; } = "NONE";
    public int MaxRecords { get; set; } = 50000;
    public string SortColumn { get; set; } = "RegistrationDate";
    public string SortDirection { get; set; } = "DESC";
    public bool IncludeHistory { get; set; } = false;
    public string FollowedByFilter { get; set; } = "All";
    public string Category1Filter { get; set; } = "All";
    public string Category2Filter { get; set; } = "All";
    public List<string> CustomerCodes { get; set; } = new();
    public bool CustomerExcludeMode { get; set; } = false;
}
