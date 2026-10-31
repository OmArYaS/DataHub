using System.Text.Json;
using System.Text.Json.Serialization;

using DataHub.Application.Common;
using DataHub.Domain.Rules;

using Microsoft.AspNetCore.Mvc;

namespace DataHub.Api.Middleware;

/// <summary>
/// One envelope for the whole API: failures become ProblemDetails carrying
/// their stable `code` extension member — the webapp never parses strings
/// to learn what went wrong, and Postman can assert on the code.
/// </summary>
public sealed class ExceptionHandlingMiddleware(ILogger<ExceptionHandlingMiddleware> logger) : IMiddleware
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public async Task InvokeAsync(HttpContext context, RequestDelegate next)
    {
        try
        {
            await next(context);
        }
        // Deliberate rejections only — anything else falls through to the
        // default 500 developer path.
        catch (Exception ex) when (ex is BusinessRuleException
            or NotFoundException
            or ReadOnlyQueryException
            or UnauthorizedAccessException)
        {
#pragma warning disable CA1848, CA1873 // cold-path rejection logging, kept simple
            logger.LogInformation("API rejection {Kind}: {Message}", ex.GetType().Name, ex.Message);
#pragma warning restore CA1848, CA1873

            await WriteProblemAsync(context, ex);
        }
    }

    private static Task WriteProblemAsync(HttpContext context, Exception ex)
    {
        var (status, code, title) = ex switch
        {
            BusinessRuleException br => (br.StatusCode, br.Code, br.Message),
            ReadOnlyQueryException q => (422, q.Code, q.Message),
            UnauthorizedAccessException => (401, "CREDENTIALS_REJECTED", ex.Message),
            NotFoundException => (404, "NOT_FOUND", ex.Message),
            _ => (500, "HUB_INTERNAL", "An unexpected error occurred.")
        };

        context.Response.StatusCode = status;
        context.Response.ContentType = "application/problem+json";

        var problem = new ProblemDetails
        {
            Status = status,
            Title = title,
            Type = $"https://datahub.local/errors/{code}"
        };
        problem.Extensions["code"] = code;

        return context.Response.WriteAsync(JsonSerializer.Serialize(problem, JsonOptions));
    }
}
