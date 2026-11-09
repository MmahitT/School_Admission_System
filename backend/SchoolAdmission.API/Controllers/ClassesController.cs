using Microsoft.AspNetCore.Mvc;
using SchoolAdmission.Application.Contracts;
using SchoolAdmission.Application.Services;

namespace SchoolAdmission.API.Controllers;

[ApiController]
[Route("classes")]
public class ClassesController(IClassService classes, IHoldService holds) : ControllerBase
{
    public const string StudentHeader = "X-Student-Id";

    [HttpPost]
    public async Task<ActionResult<ClassResponse>> Create([FromBody] CreateClassRequest request, CancellationToken ct)
    {
        var created = await classes.CreateAsync(request, ct);
        return CreatedAtAction(nameof(Get), new { classId = created.Id }, created);
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ClassDetailsResponse>>> List(CancellationToken ct) =>
        Ok(await classes.ListAsync(ct));

    [HttpGet("{classId}")]
    public async Task<ActionResult<ClassDetailsResponse>> Get(Guid classId, CancellationToken ct) =>
        Ok(await classes.GetAsync(classId, ct));

    [HttpPost("{classId}/holds")]
    public async Task<ActionResult<CreateHoldResponse>> CreateHold(
        Guid classId, [FromHeader(Name = StudentHeader)] string? studentId, CancellationToken ct)
    {
        var hold = await holds.CreateAsync(classId, studentId, ct);
        return CreatedAtAction(nameof(HoldsController.Get), "Holds", new { holdId = hold.HoldId }, hold);
    }
}
