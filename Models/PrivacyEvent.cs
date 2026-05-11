using System;

namespace Privacy.Models;

[Serializable]
public sealed class PrivacyEvent
{
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;
    public string ContactId { get; set; } = string.Empty;
    public string ContactName { get; set; } = string.Empty;
    public string ContactWorld { get; set; } = string.Empty;
    public string Kind { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
}
