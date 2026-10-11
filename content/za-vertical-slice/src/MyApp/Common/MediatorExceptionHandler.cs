using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using ZeroAlloc.Mediator.Authorization;
using ZeroAlloc.Mediator.Validation;

namespace MyApp.Common;

/// <summary>
/// Turns the exceptions the mediator pipeline behaviors throw into problem responses.
/// The validation and authorization behaviors return a failed result only when the
/// response is <c>Result&lt;T, ValidationError&gt;</c> or <c>Result&lt;T, AuthorizationFailure&gt;</c>;
/// for any other error type, such as this app's <see cref="Error"/>, they throw instead.
/// Without this handler a rejected request would surface as a 500.
/// </summary>
public sealed class MediatorExceptionHandler : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        IResult? result = exception switch
        {
            ValidationFailedException validation => ToValidationProblem(validation.Error),
            AuthorizationDeniedException denied => Results.Problem(
                detail: denied.Failure.Reason,
                statusCode: StatusCodes.Status403Forbidden,
                title: denied.Failure.Code),
            _ => null,
        };

        if (result is null)
            return false;

        await result.ExecuteAsync(httpContext).ConfigureAwait(false);
        return true;
    }

    private static IResult ToValidationProblem(ZeroAlloc.Mediator.Validation.ValidationError error)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        foreach (var failure in error.Failures)
        {
            errors[failure.PropertyName] = errors.TryGetValue(failure.PropertyName, out var existing)
                ? [.. existing, failure.ErrorMessage]
                : [failure.ErrorMessage];
        }

        return TypedResults.ValidationProblem(errors, title: "validation.failed");
    }
}
