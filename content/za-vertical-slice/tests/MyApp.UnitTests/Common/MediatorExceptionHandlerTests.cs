using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using MyApp.Common;
using Xunit;
using ZeroAlloc.Authorization;
using ZeroAlloc.Mediator.Authorization;
using ZeroAlloc.Mediator.Validation;
using ZeroAlloc.Validation;

namespace MyApp.UnitTests.Common;

/// <summary>
/// The validation and authorization behaviors throw for this app's <see cref="Error"/> result type,
/// so this handler decides what the client sees. The endpoint policies normally reject a request
/// before the mediator's own policies run, so the mappings are exercised directly.
/// </summary>
public sealed class MediatorExceptionHandlerTests
{
    [Fact]
    public async Task Maps_an_authorization_denial_to_403_with_its_code_and_reason()
    {
        var denied = new AuthorizationDeniedException(new AuthorizationFailure("orders.denied", "Missing orders.read scope"));

        var (handled, status, body) = await HandleAsync(denied);

        Assert.True(handled);
        Assert.Equal(StatusCodes.Status403Forbidden, status);
        Assert.Equal("orders.denied", body.GetProperty("title").GetString());
        Assert.Equal("Missing orders.read scope", body.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task Maps_a_validation_failure_to_400_grouped_by_property()
    {
        var error = new ZeroAlloc.Mediator.Validation.ValidationError(
        [
            new ValidationFailure { PropertyName = "Total", ErrorMessage = "must be positive" },
            new ValidationFailure { PropertyName = "Total", ErrorMessage = "too many decimals" },
            new ValidationFailure { PropertyName = "CustomerId", ErrorMessage = "required" },
        ]);

        var (handled, status, body) = await HandleAsync(new ValidationFailedException(error));

        Assert.True(handled);
        Assert.Equal(StatusCodes.Status400BadRequest, status);
        Assert.Equal("validation.failed", body.GetProperty("title").GetString());
        var errors = body.GetProperty("errors");
        Assert.Equal(["must be positive", "too many decimals"], errors.GetProperty("Total").EnumerateArray().Select(e => e.GetString()));
        Assert.Equal(["required"], errors.GetProperty("CustomerId").EnumerateArray().Select(e => e.GetString()));
    }

    [Fact]
    public async Task Leaves_other_exceptions_to_the_next_handler()
    {
        var (handled, status, _) = await HandleAsync(new InvalidOperationException("boom"));

        Assert.False(handled);
        Assert.Equal(StatusCodes.Status200OK, status);
    }

    private static async Task<(bool Handled, int Status, JsonElement Body)> HandleAsync(Exception exception)
    {
        var context = new DefaultHttpContext
        {
            RequestServices = new ServiceCollection().AddLogging().BuildServiceProvider(),
        };
        context.Response.Body = new MemoryStream();

        var handled = await new MediatorExceptionHandler().TryHandleAsync(context, exception, CancellationToken.None);

        context.Response.Body.Position = 0;
        var body = context.Response.Body.Length == 0
            ? default
            : (await JsonDocument.ParseAsync(context.Response.Body)).RootElement.Clone();
        return (handled, context.Response.StatusCode, body);
    }
}
