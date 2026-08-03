using System.ComponentModel.DataAnnotations;

namespace Lamie.Domain.Entities;

public enum EntityStatus : int
{
    [Display(Name = "Inactive")]
    Inactive = 0,

    [Display(Name = "Active")]
    Active = 1,

    [Display(Name = "Deleted")]
    Deleted = 99,

    [Display(Name = "Default")]
    Default = 3,

    [Display(Name = "New")]
    New = 4,

    [Display(Name = "Edited")]
    Edited = 5,

    [Display(Name = "Pending")]
    Pending = 6,

    [Display(Name = "Canceled")]
    Canceled = 7,

    [Display(Name = "Unchanged")]
    Unchanged = 8,
}
public abstract class Entity
{
    //public virtual EntityStatus? RecordStatus { get; set; } = EntityStatus.Inactive;
    public virtual int? CreatedBy { get; set; }
    public virtual string? CreatedName { get; set; } = string.Empty;
    public virtual DateTime? CreatedAt { get; set; }
    public virtual int? UpdatedBy { get; set; }
    public virtual string? UpdatedName { get; set; } = string.Empty;
    public virtual DateTime? UpdatedAt { get; set; }

    public static readonly HashSet<string> NonUpdatableProperties = new(StringComparer.OrdinalIgnoreCase)
        {
            nameof(CreatedBy),
            nameof(CreatedAt),
            nameof(CreatedName)
        };
}

