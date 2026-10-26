namespace HelpDesk.Domain.Areas;

/// <summary>
/// A team that attends tickets (Customer Happiness, Finance, IT, Development...).
/// All areas share one database and one ticket; an area only owns the segments it works on.
/// </summary>
public class Area
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public string? Description { get; set; }

    /// <summary>Shown to requesters in "¿Con quién necesitas ayuda?". Internal-only areas (e.g. Development) set it to false.</summary>
    public bool AcceptsRequests { get; set; } = true;

    public bool IsActive { get; set; } = true;

    public List<AreaMember> Members { get; set; } = [];
    public List<Category> Categories { get; set; } = [];
}

public class AreaMember
{
    public int AreaId { get; set; }
    public Guid UserId { get; set; }

    /// <summary>Area leads can reassign work and see the area's metrics.</summary>
    public bool IsLead { get; set; }
}

public class Category
{
    public int Id { get; set; }
    public int AreaId { get; set; }
    public int? ParentId { get; set; }
    public required string Name { get; set; }
    public bool IsActive { get; set; } = true;
}
