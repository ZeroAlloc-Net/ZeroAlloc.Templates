using Microsoft.AspNetCore.OutputCaching;

namespace MyApp.Api;

/// <summary>
/// Output-cache policy enabling caching for authenticated GET/HEAD requests.
/// The framework's DefaultPolicy refuses to cache when an Authorization header
/// is present — too strict for this template's GET endpoints (which will land
/// in later tasks). Use only for endpoints whose response body is
/// identity-independent.
/// </summary>
internal sealed class CacheAuthenticatedGetsPolicy : IOutputCachePolicy
{
    public ValueTask CacheRequestAsync(OutputCacheContext context, CancellationToken cancellation)
    {
        var method = context.HttpContext.Request.Method;
        var cacheable = HttpMethods.IsGet(method) || HttpMethods.IsHead(method);
        context.EnableOutputCaching = cacheable;
        context.AllowCacheLookup = cacheable;
        context.AllowCacheStorage = cacheable;
        context.AllowLocking = true;
        return ValueTask.CompletedTask;
    }

    public ValueTask ServeFromCacheAsync(OutputCacheContext context, CancellationToken cancellation)
        => ValueTask.CompletedTask;

    public ValueTask ServeResponseAsync(OutputCacheContext context, CancellationToken cancellation)
    {
        var response = context.HttpContext.Response;
        if (response.StatusCode != StatusCodes.Status200OK)
        {
            context.AllowCacheStorage = false;
            return ValueTask.CompletedTask;
        }
        if (response.Headers.ContainsKey("Set-Cookie"))
        {
            context.AllowCacheStorage = false;
            return ValueTask.CompletedTask;
        }
        return ValueTask.CompletedTask;
    }
}
