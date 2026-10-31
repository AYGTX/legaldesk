using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SignFlow.Application.Common.Exceptions;
using SignFlow.Application.Common.Interfaces;
using SignFlow.Application.Common.Services;
using SignFlow.Domain.Exceptions;

namespace SignFlow.Api.Middleware;

public class ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unhandled exception: {Message}", ex.Message);
            await HandleExceptionAsync(context, ex);
        }
    }

    private static async Task HandleExceptionAsync(HttpContext context, Exception exception)
    {
        var (statusCode, problem) = exception switch
        {
            ValidationException ve => (HttpStatusCode.UnprocessableEntity, new ProblemDetails
            {
                Title = "Validation Failed",
                Status = 422,
                Detail = "One or more validation errors occurred.",
                Extensions = { ["errors"] = ve.Errors }
            }),
            NotFoundException nfe => (HttpStatusCode.NotFound, new ProblemDetails
            {
                Title = "Resource Not Found",
                Status = 404,
                Detail = nfe.Message
            }),
            ForbiddenAccessException fae => (HttpStatusCode.Forbidden, fae.Code is null
                ? new ProblemDetails { Title = "Forbidden", Status = 403, Detail = fae.Message }
                : new ProblemDetails { Title = "Forbidden", Status = 403, Detail = fae.Message, Extensions = { ["code"] = fae.Code } }),
            UnauthorizedAccessException uae => (HttpStatusCode.Unauthorized, new ProblemDetails
            {
                Title = "Unauthorized",
                Status = 401,
                Detail = uae.Message
            }),
            ConflictException { Code: not null } ce => (HttpStatusCode.Conflict, new ProblemDetails
            {
                Title = "Conflict",
                Status = 409,
                Detail = ce.Message,
                Extensions = { ["code"] = ce.Code, ["params"] = ce.Parameters }
            }),
            ConflictException ce => (HttpStatusCode.Conflict, new ProblemDetails
            {
                Title = "Conflict",
                Status = 409,
                Detail = ce.Message
            }),
            SignFlow.Application.Features.Signing.SigningRefusedException sre => ((HttpStatusCode)sre.Status, new ProblemDetails
            {
                Title = "Signing refused",
                Status = sre.Status,
                Detail = sre.Message,
                Extensions = { ["code"] = sre.Code }
            }),
            ServiceUnavailableException sue => (HttpStatusCode.ServiceUnavailable, new ProblemDetails
            {
                Title = "Service unavailable",
                Status = 503,
                Detail = sue.Message,
                Extensions = { ["code"] = sue.Code }
            }),
            InvalidStatusTransitionException iste => (HttpStatusCode.Conflict, new ProblemDetails
            {
                Title = "Conflict",
                Status = 409,
                Detail = iste.Message,
                Extensions = { ["code"] = InvalidStatusTransitionException.Code, ["from"] = iste.From.ToString(), ["to"] = iste.To.ToString() }
            }),
            DbUpdateConcurrencyException =>(HttpStatusCode.Conflict, new ProblemDetails
            {
                Title = "Concurrency Conflict",
                Status = 409,
                Detail = "The item was changed by someone else while you were working. Please refresh and try again."
            }),
            AiNotConfiguredException => (HttpStatusCode.ServiceUnavailable, new ProblemDetails
            {
                Title = "Harvey not configured",
                Status = 503,
                Detail = "Harvey is not configured on this server."
            }),
            AiResponseUnusableException arue => (HttpStatusCode.BadGateway, new ProblemDetails
            {
                Title = "Harvey response unusable",
                Status = 502,
                Detail = arue.Message
            }),
            ScannerUnavailableException sue => (HttpStatusCode.ServiceUnavailable, new ProblemDetails
            {
                Title = "Virus scanner unavailable",
                Status = 503,
                Detail = sue.Message,
                Extensions = { ["code"] = ScannerUnavailableException.Code }
            }),
            // Security baseline: an unexpected failure's own message (paths, SQL, internals) never reaches the
            // client; it is in the log under the trace id returned below.
            _ => (HttpStatusCode.InternalServerError, new ProblemDetails
            {
                Title = "Internal Server Error",
                Status = 500,
                Detail = "An unexpected error occurred. Please try again later."
            })
        };

        problem.Instance = context.Request.Path;
        problem.Extensions["traceId"] = context.TraceIdentifier;

        context.Response.StatusCode = (int)statusCode;
        context.Response.ContentType = "application/problem+json";

        var json = JsonSerializer.Serialize(problem, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
        });

        await context.Response.WriteAsync(json);
    }
}
