using Microsoft.AspNetCore.Http;

namespace MyApp.Common;

/// <summary>
/// Discriminator for the small fixed set of failure modes a slice can return.
/// Maps 1:1 to HTTP status codes via <see cref="ErrorExtensions.ToProblem"/>.
/// </summary>
public enum ErrorKind
{
    /// <summary>Generic failure that doesn't fit another category — maps to 500.</summary>
    Failure = 0,

    /// <summary>Validator rejected the request — maps to 400.</summary>
    ValidationFailed = 1,

    /// <summary>Target entity does not exist — maps to 404.</summary>
    NotFound = 2,

    /// <summary>Caller is not authenticated or lacks the required scope — maps to 401/403.</summary>
    Unauthorized = 3,

    /// <summary>Operation rejected because the current state forbids it — maps to 409.</summary>
    Conflict = 4,
}

/// <summary>
/// Slice-layer failure envelope. Pair with <see cref="ZeroAlloc.Results.Result{T, TError}"/>
/// in handler signatures, e.g. <c>Result&lt;OrderId, Error&gt;</c>. Endpoint code uses
/// <see cref="ErrorExtensions.ToProblem"/> to materialise an <see cref="IResult"/>
/// with the right status code and RFC 7807 ProblemDetails body.
/// </summary>
/// <param name="Kind">Failure category that drives the HTTP status code.</param>
/// <param name="Code">Stable machine-readable code (e.g. <c>"order.not_found"</c>).</param>
/// <param name="Message">Human-readable description suitable for logging or display.</param>
public readonly record struct Error(ErrorKind Kind, string Code, string Message)
{
    public static Error NotFound(string code, string message) =>
        new(ErrorKind.NotFound, code, message);

    public static Error ValidationFailed(string code, string message) =>
        new(ErrorKind.ValidationFailed, code, message);

    public static Error Unauthorized(string code, string message) =>
        new(ErrorKind.Unauthorized, code, message);

    public static Error Conflict(string code, string message) =>
        new(ErrorKind.Conflict, code, message);

    public static Error Failure(string code, string message) =>
        new(ErrorKind.Failure, code, message);
}

/// <summary>
/// First-failure validation error returned by a slice's validator. Sliceable into an
/// <see cref="Error"/> via <c>Error.ValidationFailed(...)</c> when the handler converts
/// the validator outcome into a Result.
/// </summary>
/// <param name="Field">Property/field name that failed validation.</param>
/// <param name="Message">Human-readable validation message.</param>
public sealed record ValidationError(string Field, string Message);

/// <summary>
/// Endpoint-layer extensions that convert slice failures into ASP.NET Core
/// <see cref="IResult"/> instances with the right status code.
/// </summary>
public static class ErrorExtensions
{
    /// <summary>
    /// Convert an <see cref="Error"/> to an ASP.NET Core <see cref="IResult"/> with a
    /// status code that matches its <see cref="ErrorKind"/>. The returned result writes
    /// an RFC 7807 ProblemDetails body — endpoints can surface it directly:
    /// <code>
    /// var result = await mediator.Send(cmd, ct);
    /// return result.Match(id => Results.Created(...), err => err.ToProblem());
    /// </code>
    /// </summary>
    public static IResult ToProblem(this Error error)
    {
        var statusCode = error.Kind switch
        {
            ErrorKind.ValidationFailed => StatusCodes.Status400BadRequest,
            ErrorKind.Unauthorized => StatusCodes.Status401Unauthorized,
            ErrorKind.NotFound => StatusCodes.Status404NotFound,
            ErrorKind.Conflict => StatusCodes.Status409Conflict,
            _ => StatusCodes.Status500InternalServerError,
        };

        return Results.Problem(
            detail: error.Message,
            statusCode: statusCode,
            title: error.Code);
    }

    /// <summary>
    /// Convert a <see cref="ValidationError"/> to a 400 Bad Request <see cref="IResult"/>.
    /// Convenience for endpoints that surface validation failures without going through
    /// the full <see cref="Error"/> envelope.
    /// </summary>
    public static IResult ToProblem(this ValidationError validationError) =>
        Results.Problem(
            detail: $"{validationError.Field}: {validationError.Message}",
            statusCode: StatusCodes.Status400BadRequest,
            title: "validation.failed");
}
