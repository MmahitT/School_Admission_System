using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using SchoolAdmission.Application.Common;

namespace SchoolAdmission.API.Infrastructure;

/// <summary>Single place that turns exceptions into the API's error body (RFC 7807 + stable "code").</summary>
public class GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext http, Exception exception, CancellationToken ct)
    {
        if (exception is OperationCanceledException && http.RequestAborted.IsCancellationRequested)
        {
            http.Response.StatusCode = 499; // client closed request; nothing to report
            return true;
        }

        int status;
        string code, detail;
        switch (exception)
        {
            case AdmissionException ex:
                status = ex.Kind switch
                {
                    ErrorKind.Validation => StatusCodes.Status400BadRequest,
                    ErrorKind.NotFound => StatusCodes.Status404NotFound,
                    ErrorKind.Forbidden => StatusCodes.Status403Forbidden,
                    _ => StatusCodes.Status409Conflict,
                };
                (code, detail) = (ex.Code, ex.Message);
                logger.LogInformation("Request rejected: {Code} ({Method} {Path})", code, http.Request.Method, http.Request.Path);
                break;

            case BadHttpRequestException:
                status = StatusCodes.Status400BadRequest;
                (code, detail) = (ErrorCodes.ValidationFailed, "The request could not be understood.");
                break;

            default:
                status = StatusCodes.Status500InternalServerError;
                (code, detail) = ("INTERNAL_ERROR", "Something went wrong. Please try again.");
                logger.LogError(exception, "Unhandled exception ({Method} {Path})", http.Request.Method, http.Request.Path);
                break;
        }

        http.Response.StatusCode = status;
        await http.Response.WriteAsJsonAsync(Problem(http, status, code, detail), (System.Text.Json.JsonSerializerOptions?)null,
            "application/problem+json", ct);
        return true;
    }

    public static ProblemDetails Problem(HttpContext http, int status, string code, string detail)
    {
        var problem = new ProblemDetails
        {
            Status = status,
            Title = ReasonPhrase(status),
            Detail = detail,
        };
        problem.Extensions["code"] = code;
        problem.Extensions["traceId"] = http.TraceIdentifier;
        return problem;
    }

    private static string ReasonPhrase(int status) => status switch
    {
        400 => "Bad Request",
        403 => "Forbidden",
        404 => "Not Found",
        409 => "Conflict",
        _ => "Internal Server Error",
    };
}
