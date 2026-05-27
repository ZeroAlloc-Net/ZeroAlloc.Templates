using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using MyApp.Features.Customers.GetCustomer;
using Xunit;

namespace MyApp.IntegrationTests.Features.Customers.GetCustomer;

public sealed class GetCustomerEndpointTests : IClassFixture<MyAppFactory>
{
    private readonly MyAppFactory _factory;

    public GetCustomerEndpointTests(MyAppFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task GET_customers_id_returns_200_for_known_id()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", TestJwt.Issue(["customers.write"]));

        var createResp = await client.PostAsJsonAsync("/customers", new
        {
            name = "Acme Ltd.",
            email = "billing@acme.example",
        });
        createResp.EnsureSuccessStatusCode();
        var location = createResp.Headers.Location!.ToString();

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", TestJwt.Issue(["customers.read"]));
        var resp = await client.GetAsync(location);

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var dto = await resp.Content.ReadFromJsonAsync<CustomerDto>();
        Assert.NotNull(dto);
        Assert.Equal("Acme Ltd.", dto!.Name);
        Assert.Equal("billing@acme.example", dto.Email);
    }

    [Fact]
    public async Task GET_customers_id_returns_404_for_unknown_id()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", TestJwt.Issue(["customers.read"]));

        var resp = await client.GetAsync("/customers/9999");

        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }
}
