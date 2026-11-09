using Microsoft.AspNetCore.Mvc;
using SchoolAdmission.Application.Contracts;
using SchoolAdmission.Application.Services;

namespace SchoolAdmission.API.Controllers;

[ApiController]
[Route("holds")]
public class HoldsController(IHoldService holds) : ControllerBase
{
    [HttpGet("{holdId}")]
    public async Task<ActionResult<HoldResponse>> Get(Guid holdId, CancellationToken ct) =>
        Ok(await holds.GetAsync(holdId, ct));

    [HttpPost("{holdId}/confirm")]
    public async Task<ActionResult<ConfirmHoldResponse>> Confirm(
        Guid holdId, [FromHeader(Name = ClassesController.StudentHeader)] string? studentId, CancellationToken ct) =>
        Ok(await holds.ConfirmAsync(holdId, studentId, ct));

    [HttpDelete("{holdId}")]
    public async Task<IActionResult> Cancel(
        Guid holdId, [FromHeader(Name = ClassesController.StudentHeader)] string? studentId, CancellationToken ct)
    {
        await holds.CancelAsync(holdId, studentId, ct);
        return NoContent();
    }
}
