using Microsoft.AspNetCore.OutputCaching;

namespace MyApp;

/// <summary>
/// Output-cache policy that enables caching for authenticated GET/HEAD
/// requests. The framework's DefaultPolicy refuses to cache when an
/// Authorization header is present (#189: read /orders/{id} is always
/// JWT-protected, so DefaultPolicy means zero hits). This replacement keeps
/// the other safety rails — only GET/HEAD, only 200 OK, never on Set-Cookie
/// responses — but does not vary by or skip on Authorization. Use ONLY for
/// endpoints whose response body is identity-independent.
/// </summary>
internal sealed class CacheAuthenticatedGetsPolicy : IOutputCachePolicy
{
    public CacheAuthenticatedGetsPolicy()
    {
    }

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
        // Setting a cookie on the response means the response is per-user-session;
        // refuse to share that across other readers.
        if (response.Headers.ContainsKey("Set-Cookie"))
        {
            context.AllowCacheStorage = false;
            return ValueTask.CompletedTask;
        }
        return ValueTask.CompletedTask;
    }
}
