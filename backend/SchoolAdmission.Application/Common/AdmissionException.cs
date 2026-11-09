namespace SchoolAdmission.Application.Common;

public enum ErrorKind
{
    Validation,
    NotFound,
    Forbidden,
    Conflict,
}

/// <summary>Stable machine-readable error codes returned in API error bodies.</summary>
public static class ErrorCodes
{
    public const string InvalidStudentId = "INVALID_STUDENT_ID";
    public const string ValidationFailed = "VALIDATION_FAILED";
    public const string ClassNotFound = "CLASS_NOT_FOUND";
    public const string HoldNotFound = "HOLD_NOT_FOUND";
    public const string NoSeatsAvailable = "NO_SEATS_AVAILABLE";
    public const string ActiveHoldExists = "ACTIVE_HOLD_EXISTS";
    public const string AlreadyApplied = "ALREADY_APPLIED";
    public const string HoldExpired = "HOLD_EXPIRED";
    public const string HoldCancelled = "HOLD_CANCELLED";
    public const string HoldConfirmed = "HOLD_CONFIRMED";
    public const string NotHoldOwner = "NOT_HOLD_OWNER";
    public const string DuplicateClassName = "DUPLICATE_CLASS_NAME";
}

/// <summary>An expected business/validation failure. The message is safe to show to end users.</summary>
public class AdmissionException(ErrorKind kind, string code, string message) : Exception(message)
{
    public ErrorKind Kind { get; } = kind;
    public string Code { get; } = code;

    public static AdmissionException InvalidStudentId() => new(ErrorKind.Validation, ErrorCodes.InvalidStudentId,
        "Please enter a valid 17-digit birth registration number.");
    public static AdmissionException Validation(string message) => new(ErrorKind.Validation, ErrorCodes.ValidationFailed, message);
    public static AdmissionException ClassNotFound() => new(ErrorKind.NotFound, ErrorCodes.ClassNotFound, "The requested class was not found.");
    public static AdmissionException HoldNotFound() => new(ErrorKind.NotFound, ErrorCodes.HoldNotFound, "The requested reservation was not found.");
    public static AdmissionException NoSeats() => new(ErrorKind.Conflict, ErrorCodes.NoSeatsAvailable, "No seats are currently available for this class.");
    public static AdmissionException ActiveHoldExists() => new(ErrorKind.Conflict, ErrorCodes.ActiveHoldExists, "You already have an active admission reservation.");
    public static AdmissionException AlreadyApplied() => new(ErrorKind.Conflict, ErrorCodes.AlreadyApplied, "You have already applied to this class.");
    public static AdmissionException HoldExpired() => new(ErrorKind.Conflict, ErrorCodes.HoldExpired, "Your reservation has expired. Please apply again if seats are available.");
    public static AdmissionException HoldCancelled() => new(ErrorKind.Conflict, ErrorCodes.HoldCancelled, "This reservation was cancelled and cannot be used.");
    public static AdmissionException HoldConfirmed() => new(ErrorKind.Conflict, ErrorCodes.HoldConfirmed, "A confirmed admission cannot be cancelled.");
    public static AdmissionException NotHoldOwner() => new(ErrorKind.Forbidden, ErrorCodes.NotHoldOwner, "This reservation belongs to a different student.");
    public static AdmissionException DuplicateClassName() => new(ErrorKind.Conflict, ErrorCodes.DuplicateClassName, "A class with this name already exists.");
}
