namespace SchoolAdmission.Application.Contracts;

public record CreateClassRequest(string? Name, int TotalSeats);

public record ClassResponse(Guid Id, string Name, int TotalSeats);

public record ClassDetailsResponse(
    Guid Id, string Name, int TotalSeats, int HeldSeats, int ConfirmedSeats, int AvailableSeats);

public record CreateHoldResponse(Guid HoldId, Guid ClassId, string StudentId, string Status, DateTime ExpiresAt);

public record HoldResponse(Guid HoldId, Guid ClassId, string Status, DateTime ExpiresAt);

public record ConfirmHoldResponse(Guid HoldId, string Status);

public record MyHoldResponse(Guid HoldId, Guid ClassId, string ClassName, string Status, DateTime ExpiresAt);
