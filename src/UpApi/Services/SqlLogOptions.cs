namespace UpApi.Services;

public sealed class SqlLogOptions
{
    public const string SectionName = "SqlLog";

    public string ConnectionString { get; set; } = string.Empty;
    public string Schema { get; set; } = "dbo";
    public string TableName { get; set; } = "log";
    public bool CleanupEnabled { get; set; } = true;
    public int RetentionDays { get; set; } = 30;
    public int CleanupBatchSize { get; set; } = 5000;
}
