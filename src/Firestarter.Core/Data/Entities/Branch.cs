namespace Firestarter.Core.Data.Entities;

public class Branch
{
    public int Id { get; set; }
    public int ProjectId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Sha { get; set; } = string.Empty;
    public bool IsDefault { get; set; }
    public bool IsProtected { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public Project? Project { get; set; }
}
