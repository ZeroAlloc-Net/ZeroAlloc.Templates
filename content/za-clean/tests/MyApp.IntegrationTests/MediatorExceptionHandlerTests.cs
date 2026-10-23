using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using MyApp.Api.Authorization;
using Xunit;
using ZeroAlloc.Authorization;
using ZeroAlloc.Mediator.Authorization;

namespace MyApp.IntegrationTests;

/// <summary>
/// The endpoint policies normally reject a request before the mediator's own policies run, so
/// this mapping is exercised directly: it is what a client sees when the two layers disagree.
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
