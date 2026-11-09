using SchoolAdmission.Application.Contracts;

namespace SchoolAdmission.Application.Services;

public interface IHoldService
{
    Task<CreateHoldResponse> CreateAsync(Guid classId, string? studentId, CancellationToken ct);
    Task<HoldResponse> GetAsync(Guid holdId, CancellationToken ct);
    Task<ConfirmHoldResponse> ConfirmAsync(Guid holdId, string? studentId, CancellationToken ct);
    Task CancelAsync(Guid holdId, string? studentId, CancellationToken ct);
    Task<IReadOnlyList<MyHoldResponse>> ListForStudentAsync(string? studentId, CancellationToken ct);
}
