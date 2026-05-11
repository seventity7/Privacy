using System;
using System.Collections.Generic;

namespace Privacy.Models;

[Serializable]
public sealed class PrivateGroup
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "New Group";
    public string ColorHex { get; set; } = "#FFD56A";
    public bool Expanded { get; set; } = true;
    public bool EnableStatusNotification { get; set; }
    public List<string> ContactIds { get; set; } = new();
}
