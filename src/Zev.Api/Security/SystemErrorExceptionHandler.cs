using System.Security.Claims;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Zev.Api.Data;
using Zev.Api.Domain;

namespace Zev.Api.Security;

// Records unhandled API exceptions (HTTP 500) in SystemErrorLogs and answers with ProblemDetails carrying the
// error id, so a user can quote it. Uses its own DbContext scope: the request's context may hold failed changes.
public sealed class SystemErrorExceptionHandler(IServiceScopeFactory scopes, IProblemDetailsService problemDetails,
    ILogger<SystemErrorExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken cancellationToken)
    {
        int? errorId = null;
        try
        {
            using var scope = scopes.CreateScope();
            var database = scope.ServiceProvider.GetRequiredService<ZevDbContext>();
            var entry = new SystemErrorLog
            {
                Title = SystemErrorLog.Clip($"{context.Request.Method} {context.Request.Path}", SystemErrorLog.TitleLength),
                Source = SystemErrorSource.Backend,
                ErrorType = SystemErrorLog.Clip(exception.GetType().FullName, SystemErrorLog.ErrorTypeLength),
                Message = SystemErrorLog.Clip(exception.Message, SystemErrorLog.MessageLength),
                StackTrace = SystemErrorLog.Clip(exception.ToString(), SystemErrorLog.StackTraceLength),
                RequestPath = SystemErrorLog.Clip(context.Request.Path.Value, SystemErrorLog.PathLength),
                UserId = Guid.TryParse(context.User.FindFirstValue("sub"), out var userId) ? userId : null
            };
            database.SystemErrorLogs.Add(entry);
            // Logged even if the request was cancelled, otherwise the failure would leave no trace.
            await database.SaveChangesAsync(CancellationToken.None);
            errorId = entry.Id;
        }
        catch (Exception logging)
        {
            logger.LogError(logging, "Recording an unhandled exception in SystemErrorLogs failed.");
        }
        logger.LogError(exception, "Unhandled exception {ErrorId} on {Method} {Path}", errorId, context.Request.Method,
            context.Request.Path);
        context.Response.StatusCode = StatusCodes.Status500InternalServerError;
        var problem = new ProblemDetails { Status = 500, Title = "Interna greška servera.",
            Detail = errorId is null ? null : $"Greška je evidentirana pod brojem {errorId}." };
        if (errorId is not null) problem.Extensions["errorId"] = errorId;
        return await problemDetails.TryWriteAsync(new ProblemDetailsContext { HttpContext = context, Exception = exception,
            ProblemDetails = problem });
    }
}
