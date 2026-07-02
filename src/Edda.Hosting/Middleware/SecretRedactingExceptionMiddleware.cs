using Edda.Security.OutputFilter;
using Microsoft.Extensions.Logging;

namespace Edda.Hosting.Middleware;

/// <summary>
/// Terminal exception-handling middleware that redacts secrets from an unhandled exception before it is
/// logged — API keys or tokens can appear in an exception message or a request URL — and then returns an
/// RFC 7807 ProblemDetails 500 response without leaking internal detail. Used in place of the framework
/// exception handler in non-development environments so that secrets never reach the logs (issue A10).
/// </summary>
public sealed class SecretRedactingExceptionMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ISecretRedactor _redactor;
    private readonly ILogger<SecretRedactingExceptionMiddleware> _logger;

    /// <summary>Initializes a new instance of the <see cref="SecretRedactingExceptionMiddleware"/> class.</summary>
    /// <param name="next">The next middleware in the request pipeline.</param>
    /// <param name="redactor">Secret redactor applied to the exception text before it is logged.</param>
    /// <param name="logger">Logger for the redacted exception.</param>
    public SecretRedactingExceptionMiddleware(
        RequestDelegate next,
        ISecretRedactor redactor,
        ILogger<SecretRedactingExceptionMiddleware> logger)
    {
        _next = next;
        _redactor = redactor;
        _logger = logger;
    }

    /// <summary>Invokes the middleware, catching and redact-logging any unhandled exception.</summary>
    /// <param name="context">The current HTTP context.</param>
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // The exception object is deliberately NOT passed to the logger (that would render its raw
            // ToString); instead the redacted text is logged as a structured value.
            _logger.LogError(
                "Unhandled exception while processing {Method} {Path}: {Exception}",
                context.Request.Method,
                context.Request.Path,
                ExceptionRedactor.RedactForLog(_redactor, ex));

            if (context.Response.HasStarted)
            {
                throw;
            }

            context.Response.Clear();
            await Results
                .Problem(statusCode: StatusCodes.Status500InternalServerError, title: "An unexpected error occurred.")
                .ExecuteAsync(context)
                .ConfigureAwait(false);
        }
    }
}
