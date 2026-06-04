using System.Security.Claims;
using BenchmarkDotNet.Attributes;
using MyApp.Api.Authorization;
using MyApp.Application.Authorization;
using ZeroAlloc.Authorization;

namespace MyApp.Benchmarks;

/// <summary>
/// Allocation benchmark for <see cref="ClaimsPrincipalSecurityContext"/> +
/// <see cref="OrdersReadPolicy.HasScope"/>. Validates the #172 rewrite:
/// single-value claim TryGetValue is zero-alloc on the hot path; multi-value
/// allocates only the joined string (RFC 6749 §3.3).
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

    /// <summary>Single-value scope claim. Target: 0 B/op.</summary>
    [Benchmark]
    public bool HasScope_SingleValue() => OrdersReadPolicy.HasScope(_singleScopeCtx, "orders.read");

    /// <summary>Multi-value (two separate "scope" claims). One joined string only.</summary>
    [Benchmark]
    public bool HasScope_MultiValueClaims() => OrdersReadPolicy.HasScope(_multiScopeCtx, "orders.read");

    /// <summary>Missing claim — early-out path. Target: 0 B/op.</summary>
    [Benchmark]
    public bool HasScope_Missing() => OrdersReadPolicy.HasScope(_noScopeCtx, "orders.read");
}
