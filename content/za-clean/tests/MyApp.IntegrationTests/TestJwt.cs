using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace MyApp.IntegrationTests;

internal static class TestJwt
{
    // Must match the value supplied to the test host via WebApplicationFactory configuration.
    public const string DevKey = "DEV-ONLY-KEY-DO-NOT-USE-IN-PRODUCTION-AT-LEAST-32-CHARS-LONG";

    public static string Issue(IEnumerable<string>? scopes = null)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(DevKey));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var claims = (scopes ?? Array.Empty<string>())
            .Select(s => new Claim("scope", s))
            .Append(new Claim("sub", "test-user"))
            .ToArray();
        var token = new JwtSecurityToken(
            claims: claims,
            signingCredentials: creds,
            expires: DateTime.UtcNow.AddMinutes(5));
        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
