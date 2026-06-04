using System.Security.Claims;
using BenchmarkDotNet.Attributes;
using MyApp.Authorization;
using ZeroAlloc.Authorization;

namespace MyApp.Benchmarks;

/// <summary>
/// Allocation benchmark for <see cref="ClaimsPrincipalSecurityContext"/> +
/// <see cref="OrdersReadPolicy.HasScope"/>. Validates the #172 rewrite:
/// single-value claim TryGetValue is zero-collection-alloc on the hot path;
/// the residual ~40-180 B per call is the enumerator state machine for
/// ClaimsPrincipal.Claims (an iterator method on the public API).
/// </summary>
[MemoryDiagnoser]
public class SecurityContextBench
{
    private ISecurityContext _singleScopeCtx = null!;
    private ISecurityContext _multiScopeCtx = null!;
    private ISecurityContext _noScopeCtx = null!;

    [GlobalSetup]
    public void Setup()
    {
        _singleScopeCtx = new ClaimsPrincipalSecurityContext(new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.Name, "alice"),
            new Claim("scope", "orders.read orders.write"),
        }, "test")));

        _multiScopeCtx = new ClaimsPrincipalSecurityContext(new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.Name, "alice"),
            new Claim("scope", "orders.read"),
            new Claim("scope", "orders.write"),
        }, "test")));

        _noScopeCtx = new ClaimsPrincipalSecurityContext(new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.Name, "alice"),
        }, "test")));
    }

    [Benchmark]
    public bool HasScope_SingleValue() => OrdersReadPolicy.HasScope(_singleScopeCtx, "orders.read");

    [Benchmark]
    public bool HasScope_MultiValueClaims() => OrdersReadPolicy.HasScope(_multiScopeCtx, "orders.read");

    [Benchmark]
    public bool HasScope_Missing() => OrdersReadPolicy.HasScope(_noScopeCtx, "orders.read");
}
