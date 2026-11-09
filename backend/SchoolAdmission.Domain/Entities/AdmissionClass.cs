namespace SchoolAdmission.Domain.Entities;

/// <summary>One class + shift combination with its own independent seat capacity.</summary>
public class AdmissionClass
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int TotalSeats { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public ICollection<AdmissionHold> Holds { get; set; } = new List<AdmissionHold>();
}
