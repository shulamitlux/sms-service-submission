namespace SmsService.Api.Validation;

/// <summary>
/// The two ways the API rejects invalid input:
/// <list type="bullet">
/// <item><see cref="Reject(HttpContext, IReadOnlyDictionary{string, string[]})"/>: format errors (phone format, body length,
/// id, status). The client gets the field names and reasons, so it knows what to fix. Nothing sensitive is revealed.</item>
/// <item><see cref="RejectGeneric"/>: a valid number that belongs to no employee. The client gets a generic
/// "Invalid input" only, so the response does not say "this is not an employee's number".</item>
/// </list>
/// Both log the details on the server.
/// </summary>
public static class InvalidInput
{
    public const string Title = "Invalid input";

    public static IResult Reject(HttpContext http, IReadOnlyDictionary<string, string[]> errors)
    {
        Logger(http).LogWarning("Rejected invalid input on {Method} {Path}: {Errors}",
            http.Request.Method, http.Request.Path,
            string.Join("; ", errors.Select(e => $"{e.Key}: {string.Join(", ", e.Value)}")));

        return Results.ValidationProblem(errors.ToDictionary(e => e.Key, e => e.Value));
    }

    public static IResult Reject(HttpContext http, string field, string error) =>
        Reject(http, new Dictionary<string, string[]> { [field] = [error] });

    public static IResult RejectGeneric(HttpContext http, string logReason)
    {
        Logger(http).LogWarning("Rejected input on {Method} {Path} with a generic error: {Reason}",
            http.Request.Method, http.Request.Path, logReason);

        return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: Title);
    }

    private static ILogger Logger(HttpContext http) =>
        http.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(InvalidInput));
}
