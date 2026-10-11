using Microsoft.AspNetCore.Diagnostics;
using ZeroAlloc.Mediator.Authorization;

namespace MyApp.Api.Authorization;

/// <summary>
/// Turns a mediator-level <c>[RequirePolicy]</c> denial into a 403 problem response.
/// The authorization behavior returns a failed result only when the response is
/// <c>Result&lt;T, AuthorizationFailure&gt;</c>; for this app's <c>ApplicationError</c> results
/// it throws <see cref="AuthorizationDeniedException"/> instead, which would otherwise
/// surface as a 500.
/// </summary>
public sealed class MediatorExceptionHandler : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        if (exception is not AuthorizationDeniedException denied)
            return false;

        await Results.Problem(
                detail: denied.Failure.Reason,
                statusCode: StatusCodes.Status403Forbidden,
                title: denied.Failure.Code)
            .ExecuteAsync(httpContext)
            .ConfigureAwait(false);
        return true;
    }
}
