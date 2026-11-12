using ColorMatch.Application.Common;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ColorMatch.API.Middleware;

/// <summary>Maps exceptions to RFC 7807 problem details. Unexpected errors never leak internals.</summary>
public sealed class GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger, IProblemDetailsService problems) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext http, Exception ex, CancellationToken ct)
    {
        ProblemDetails problem;
        switch (ex)
        {
            case ValidationAppException v:
                problem = new ValidationProblemDetails(v.Errors.ToDictionary(k => k.Key, k => k.Value))
                { Status = 400, Title = "Validation failed", Detail = v.Message };
                break;
            case NotFoundException:
                problem = new ProblemDetails { Status = 404, Title = "Not found", Detail = ex.Message };
                break;
            case ConflictException:
                problem = new ProblemDetails { Status = 409, Title = "Conflict", Detail = ex.Message };
                break;
            case TooManyRequestsException:
                problem = new ProblemDetails { Status = 429, Title = "Too many requests", Detail = ex.Message };
                break;
            case ForbiddenException:
                problem = new ProblemDetails { Status = 403, Title = "Forbidden", Detail = ex.Message };
                break;
            case UnauthorizedAppException:
                problem = new ProblemDetails { Status = 401, Title = "Unauthorized", Detail = ex.Message };
                break;
            case OperationCanceledException when http.RequestAborted.IsCancellationRequested:
                http.Response.StatusCode = 499; // client closed request
                return true;
            case DbUpdateException or TimeoutException or Microsoft.Data.SqlClient.SqlException:
                logger.LogError(ex, "Database failure on {Path}", http.Request.Path);
                problem = new ProblemDetails { Status = 503, Title = "Service unavailable", Detail = "The database is temporarily unavailable. Please try again." };
                break;
            default:
                logger.LogError(ex, "Unhandled exception on {Path}", http.Request.Path);
                problem = new ProblemDetails { Status = 500, Title = "Server error", Detail = "An unexpected error occurred." };
                break;
        }

        http.Response.StatusCode = problem.Status!.Value;
        return await problems.TryWriteAsync(new ProblemDetailsContext { HttpContext = http, ProblemDetails = problem, Exception = null });
    }
}
