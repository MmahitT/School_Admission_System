namespace SchoolAdmission.Domain.Entities;

/// <summary>A student's application for a seat; also the reservation and the confirmed admission.</summary>
public class AdmissionHold
{
    public Guid Id { get; set; }
    public Guid ClassId { get; set; }
    public string StudentId { get; set; } = string.Empty;
    public HoldStatus Status { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime? ConfirmedAt { get; set; }
    public DateTime? CancelledAt { get; set; }

    public AdmissionClass? Class { get; set; }
}
