using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.OutputCaching;
using MyApp.Api;
using Xunit;

namespace MyApp.IntegrationTests;

/// <summary>
/// The policy caches authenticated reads, which is only safe for responses that are the same for
/// every reader. These are the rails that keep per-caller responses out of the shared cache.
/// </summary>
public sealed class CacheAuthenticatedGetsPolicyTests
{
    private static readonly IOutputCachePolicy Policy = new CacheAuthenticatedGetsPolicy();

    [Theory]
    [InlineData("GET", true)]
    [InlineData("HEAD", true)]
    [InlineData("POST", false)]
    [InlineData("DELETE", false)]
    public async Task Only_GET_and_HEAD_are_cached(string method, bool cached)
    {
        var context = Context(method);

        await Policy.CacheRequestAsync(context, CancellationToken.None);

        Assert.Equal(cached, context.EnableOutputCaching);
        Assert.Equal(cached, context.AllowCacheLookup);
        Assert.Equal(cached, context.AllowCacheStorage);
    }

    [Fact]
    public async Task A_200_without_a_cookie_is_stored()
    {
        var context = await ServedAsync(StatusCodes.Status200OK);

        Assert.True(context.AllowCacheStorage);
    }

    [Fact]
    public async Task A_response_that_sets_a_cookie_is_not_stored()
    {
        var context = await ServedAsync(StatusCodes.Status200OK, setCookie: "session=abc");

        Assert.False(context.AllowCacheStorage);
    }

    [Fact]
    public async Task A_response_other_than_200_is_not_stored()
    {
        var context = await ServedAsync(StatusCodes.Status404NotFound);

        Assert.False(context.AllowCacheStorage);
    }

    private static OutputCacheContext Context(string method)
    {
        var http = new DefaultHttpContext();
        http.Request.Method = method;
        return new OutputCacheContext { HttpContext = http };
    }

    private static async Task<OutputCacheContext> ServedAsync(int status, string? setCookie = null)
    {
        var context = Context("GET");
        await Policy.CacheRequestAsync(context, CancellationToken.None);
        context.HttpContext.Response.StatusCode = status;
        if (setCookie is not null)
            context.HttpContext.Response.Headers.SetCookie = setCookie;

        await Policy.ServeResponseAsync(context, CancellationToken.None);
        return context;
    }
}
