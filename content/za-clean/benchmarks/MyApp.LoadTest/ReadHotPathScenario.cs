using System.Net.Http.Headers;
using System.Net.Http.Json;
using NBomber.Contracts;
using NBomber.CSharp;

namespace MyApp.LoadTest;

internal static class ReadHotPathScenario
{
    public static ScenarioProps Build(string baseUrl, string token)
    {
        var http = new HttpClient { BaseAddress = new Uri(baseUrl) };
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var seededIds = new List<int>(1000);

        return Scenario.Create("read_order_by_id", async ctx =>
        {
            if (seededIds.Count == 0)
            {
                return Response.Fail();
            }

            var id = seededIds[Random.Shared.Next(seededIds.Count)];
            var resp = await http.GetAsync($"/orders/{id}", ctx.ScenarioCancellationToken);
            return resp.IsSuccessStatusCode ? Response.Ok() : Response.Fail();
        })
        .WithInit(async ctx =>
        {
            // Seed 1000 orders via the write endpoint. If creation fails, leave seededIds empty
            // and the scenario will report failures — caller sees the issue immediately.
            for (var i = 0; i < 1000; i++)
            {
                var resp = await http.PostAsJsonAsync("/orders", new
                {
                    customerId = 1 + (i % 10),
                    items = new[] { new { sku = $"SKU-{i % 50}", quantity = 1 + (i % 3), unitPriceEur = 10m + (i % 25) } },
                    shippingZip = "1011AA",
                });

                if (resp.IsSuccessStatusCode)
                {
                    var location = resp.Headers.Location;
                    if (location is not null && int.TryParse(location.Segments[^1], out var id))
                    {
                        seededIds.Add(id);
                    }
                }
            }
        })
        .WithLoadSimulations(Simulation.KeepConstant(copies: 500, during: TimeSpan.FromSeconds(30)));
    }
}
