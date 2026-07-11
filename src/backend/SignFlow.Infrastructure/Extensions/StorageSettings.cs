using SignFlow.Domain.Enums;

namespace SignFlow.Infrastructure.Extensions;

public class StorageSettings
{
    public StorageProvider Provider { get; set; } = StorageProvider.Local;
    public string LocalPath { get; set; } = "uploads";
    public string? AzureConnectionString { get; set; }
    public string? AzureContainerName { get; set; }
    public string? AwsAccessKey { get; set; }
    public string? AwsSecretKey { get; set; }
    public string? AwsBucketName { get; set; }
    public string? AwsRegion { get; set; }
}
