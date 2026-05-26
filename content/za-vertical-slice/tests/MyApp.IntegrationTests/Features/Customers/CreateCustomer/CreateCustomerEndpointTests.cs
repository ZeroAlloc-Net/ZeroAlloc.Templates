using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Xunit;

namespace MyApp.IntegrationTests.Features.Customers.CreateCustomer;

public sealed class CreateCustomerEndpointTests : IClassFixture<MyAppFactory>
{
    private readonly MyAppFactory _factory;

    public CreateCustomerEndpointTests(MyAppFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task POST_customers_with_valid_body_and_scope_returns_201()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", TestJwt.Issue(["customers.write"]));

        var resp = await client.PostAsJsonAsync("/customers", new
        {
            name = "Acme Ltd.",
            email = "billing@acme.example",
        });

        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
        Assert.NotNull(resp.Headers.Location);
        Assert.StartsWith("/customers/", resp.Headers.Location!.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task POST_customers_without_jwt_returns_401()
    {
        var client = _factory.CreateClient();

        var resp = await client.PostAsJsonAsync("/customers", new
        {
            name = "Acme Ltd.",
            email = "billing@acme.example",
        });

        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }
}
