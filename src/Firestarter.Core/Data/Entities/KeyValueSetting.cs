namespace Firestarter.Core.Data.Entities;

public class KeyValueSetting
{
    public string Key { get; set; } = string.Empty;
    public string? Value { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
