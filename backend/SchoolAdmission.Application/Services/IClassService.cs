using SchoolAdmission.Application.Contracts;

namespace SchoolAdmission.Application.Services;

public interface IClassService
{
    Task<ClassResponse> CreateAsync(CreateClassRequest request, CancellationToken ct);
    Task<IReadOnlyList<ClassDetailsResponse>> ListAsync(CancellationToken ct);
    Task<ClassDetailsResponse> GetAsync(Guid classId, CancellationToken ct);
}
