using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SignFlow.Application.Common.Exceptions;
using SignFlow.Application.Common.Interfaces;

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
            ForbiddenAccessException fae => (HttpStatusCode.Forbidden, new ProblemDetails
            {
                Title = "Forbidden",
                Status = 403,
                Detail = fae.Message
            }),
            UnauthorizedAccessException uae => (HttpStatusCode.Unauthorized, new ProblemDetails
            {
                Title = "Unauthorized",
                Status = 401,
                Detail = uae.Message
            }),
            ConflictException ce => (HttpStatusCode.Conflict, new ProblemDetails
            {
                Title = "Conflict",
                Status = 409,
                Detail = ce.Message
            }),
            DbUpdateConcurrencyException => (HttpStatusCode.Conflict, new ProblemDetails
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
            InvalidOperationException ioe => (HttpStatusCode.BadRequest, new ProblemDetails
            {
                Title = "Invalid Operation",
                Status = 400,
                Detail = ioe.Message
            }),
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
