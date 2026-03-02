namespace Powersoft.Reporting.Web.Options;

public sealed class StorageOptions
{
    public string ServiceUrl { get; set; } = "";
    public string Region { get; set; } = "fra1";
    public string BucketName { get; set; } = "reports-ai-cold";
    public string AccessKey { get; set; } = "";
    public string SecretKey { get; set; } = "";
    /// <summary>Prefix inside the bucket (e.g. "scheduled-reports/")</summary>
    public string KeyPrefix { get; set; } = "scheduled-reports/";
}
