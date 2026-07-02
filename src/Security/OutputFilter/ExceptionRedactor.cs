namespace Edda.Security.OutputFilter;

/// <summary>
/// Produces log-safe text for an exception by routing its full string representation through an
/// <see cref="ISecretRedactor"/>, so API keys or tokens embedded in the message, URL, or inner
/// exceptions are replaced with placeholders before they reach any log sink.
/// </summary>
public static class ExceptionRedactor
{
    /// <summary>
    /// Returns the exception's full text (<see cref="System.Exception.ToString"/>, which includes the
    /// message, stack trace, and any inner exceptions) with all detected secrets redacted.
    /// </summary>
    /// <param name="redactor">The secret redactor to apply.</param>
    /// <param name="exception">The exception to render safely for logging.</param>
    /// <returns>The redacted exception text.</returns>
    public static string RedactForLog(ISecretRedactor redactor, Exception exception)
    {
        ArgumentNullException.ThrowIfNull(redactor);
        ArgumentNullException.ThrowIfNull(exception);

        // Exception.ToString() already includes the Message plus stack trace and inner exceptions, so
        // redacting it covers a secret that appears in either the message or the wider detail.
        return redactor.Redact(exception.ToString());
    }
}
