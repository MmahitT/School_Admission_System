using Microsoft.AspNetCore.Mvc;
using SchoolAdmission.Application.Contracts;
using SchoolAdmission.Application.Services;

namespace SchoolAdmission.API.Controllers;

[ApiController]
[Route("me")]
public class MeController(IHoldService holds) : ControllerBase
{
    [HttpGet("holds")]
    public async Task<ActionResult<IReadOnlyList<MyHoldResponse>>> MyHolds(
        [FromHeader(Name = ClassesController.StudentHeader)] string? studentId, CancellationToken ct) =>
        Ok(await holds.ListForStudentAsync(studentId, ct));
}
