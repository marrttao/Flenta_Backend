namespace Flenta.Infrastructure.Kyc;

public sealed class DiditOptions
{
    public bool Enabled { get; set; }
    public bool Sandbox { get; set; } = true;
    public string ApiKey { get; set; } = "";
    public string WebhookSecret { get; set; } = "";
    public string WorkflowId { get; set; } = "";
    public bool Configured => Enabled && Guid.TryParseExact(WorkflowId, "D", out var id) && id != Guid.Empty
        && !string.IsNullOrWhiteSpace(ApiKey) && ApiKey.Length <= 4096 && !ApiKey.Any(char.IsControl)
        && !string.IsNullOrWhiteSpace(WebhookSecret) && WebhookSecret.Length <= 4096;
}
