using MyApp.Api.Mappings;
using MyApp.Application.GetOrderById;
using MyApp.Domain.ValueObjects;
using Xunit;

// Pure-CLR projector tests. Lives in IntegrationTests because that's the test
// project that references MyApp.Api (where the mapper lives); MyApp.UnitTests
// is strictly layered to Domain + Application only. Mirrors MoneyConverterTests.
namespace MyApp.IntegrationTests;

public class ReadModelToResponseTests
{
    [Fact]
    public void Map_copies_flat_fields_unchanged()
    {
        var rm = new OrderReadModel(
            new OrderId(42),
            new CustomerId(7),
            "Pending",
            25m,
            "EUR",
            new[]
            {
                new OrderLineReadModel("SKU-A", 1, 10m),
                new OrderLineReadModel("SKU-B", 1, 15m),
            });

        var resp = ReadModelToResponse.Map(rm);

        Assert.Equal(new OrderId(42), resp.OrderId);
        Assert.Equal(new CustomerId(7), resp.CustomerId);
        Assert.Equal("Pending", resp.Status);
        Assert.Equal(25m, resp.Total);
        Assert.Equal("EUR", resp.Currency);
        Assert.Equal(2, resp.Lines.Count);
        Assert.Equal("SKU-A", resp.Lines[0].Sku);
        Assert.Equal(1, resp.Lines[0].Quantity);
        Assert.Equal(10m, resp.Lines[0].Price);
    }

    [Fact]
    public void Map_handles_empty_lines()
    {
        var rm = new OrderReadModel(
            new OrderId(1), new CustomerId(1), "Pending", 0m, "EUR",
            Array.Empty<OrderLineReadModel>());

        var resp = ReadModelToResponse.Map(rm);

        Assert.Empty(resp.Lines);
    }
}
