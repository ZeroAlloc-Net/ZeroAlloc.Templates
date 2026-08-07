using System.Collections.Concurrent;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Order;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace MyApp.Benchmarks;

/// <summary>
/// Measures what JWT bearer validation actually costs per request, against what a validated-token
/// cache would cost instead (issue #171).
///
/// <para>
/// The issue asks for a measurement before a decision — a cache that skips signature verification
/// is not something to add to a template on a hunch, and cache invalidation is real complexity.
/// This benchmark exists to make that call with a number rather than an intuition, and to keep the
/// answer checkable if the framework's validation cost changes.
/// </para>
///
/// <para>
/// The comparison that matters is against a whole request, which is <see cref="ReadPipelineBench"/>
/// at roughly 278 μs — not <see cref="ReadHotPathBench"/>, which times the repository read in
/// isolation and is not a per-request cost. Measured that way JWT validation is about 4% of a
/// request, which is what makes the answer no; see
/// <c>docs/benchmarks/2026-08-07-171-jwt-validation-cost.md</c>.
/// </para>
///
/// <para>
/// Uses <see cref="JsonWebTokenHandler"/> because that is what the template actually runs:
/// <c>AddJwtBearer</c> does not override the handler, and it is the framework default on .NET 8
/// and later. Benchmarking the legacy <c>JwtSecurityTokenHandler</c> would measure a path no
/// request takes.
/// </para>
/// </summary>
[MemoryDiagnoser]
[Orderer(SummaryOrderPolicy.Declared)]
public class JwtValidationBench
{
    private string _token = "";
    private string _tokenHash = "";
    private TokenValidationParameters _validationParameters = null!;
    private JsonWebTokenHandler _handler = null!;

    private readonly ConcurrentDictionary<string, ClaimsPrincipal> _byToken = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, ClaimsPrincipal> _byHash = new(StringComparer.Ordinal);

    [GlobalSetup]
    public async Task Setup()
    {
        _token = TestJwt.Issue(["orders.read"]);
        _handler = new JsonWebTokenHandler();

        // Mirrors src/MyApp.Api/Program.cs exactly. A benchmark with different validation
        // parameters would measure a different amount of work than the API performs.
        _validationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(TestJwt.DevKey)),
            ValidateIssuer = false,
            ValidateAudience = false,
            ValidateLifetime = true,
        };

        // Prime both caches with a genuinely validated principal, so the hit paths measure a
        // lookup of the same thing the miss path produces.
        var result = await _handler.ValidateTokenAsync(_token, _validationParameters);
        var principal = new ClaimsPrincipal(new ClaimsIdentity(result.ClaimsIdentity));

        _tokenHash = Hash(_token);
        _byToken[_token] = principal;
        _byHash[_tokenHash] = principal;
    }

    /// <summary>
    /// What every authenticated request costs today: signature verification, parse, and claims
    /// materialization.
    /// </summary>
    [Benchmark(Baseline = true)]
    public async Task<ClaimsPrincipal> ValidatePerRequest()
    {
        var result = await _handler.ValidateTokenAsync(_token, _validationParameters);
        return new ClaimsPrincipal(new ClaimsIdentity(result.ClaimsIdentity));
    }

    /// <summary>
    /// Validation without the final <see cref="ClaimsPrincipal"/> construction, to split the
    /// cryptographic work from the claims materialization. If the principal were the expensive
    /// part, the cheaper fix would be to reuse it rather than to cache validation outright.
    /// </summary>
    [Benchmark]
    public async Task<TokenValidationResult> ValidateOnly_NoPrincipal()
        => await _handler.ValidateTokenAsync(_token, _validationParameters);

    /// <summary>
    /// The cache design the issue proposes — keyed by a hash of the token, so raw bearer tokens
    /// are not held in memory as dictionary keys.
    /// </summary>
    [Benchmark]
    public ClaimsPrincipal CacheHit_HashKey()
    {
        var hash = Hash(_token);
        return _byHash.TryGetValue(hash, out var principal) ? principal : throw new InvalidOperationException();
    }

    /// <summary>
    /// The same lookup keyed by the token itself. Included to separate the hashing cost from the
    /// dictionary cost — if hashing dominates, the design choice matters more than the cache.
    /// </summary>
    [Benchmark]
    public ClaimsPrincipal CacheHit_TokenKey()
        => _byToken.TryGetValue(_token, out var principal) ? principal : throw new InvalidOperationException();

    private static string Hash(string token)
    {
        Span<byte> destination = stackalloc byte[32];
        SHA256.HashData(Encoding.UTF8.GetBytes(token), destination);
        return Convert.ToHexString(destination);
    }
}
