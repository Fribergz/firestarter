namespace Firestarter.Core.Data.Entities;

public class IdeRegistration
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string ExecutablePath { get; set; } = string.Empty;
    public string ArgTemplate { get; set; } = "\"{path}\"";
    public bool IsDefault { get; set; }
}
