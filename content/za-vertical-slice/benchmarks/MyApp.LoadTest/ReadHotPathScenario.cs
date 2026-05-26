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
                    total = 10m + (i % 25),
                });

                if (resp.IsSuccessStatusCode)
                {
                    // Created returns a relative Location like "/orders/123"; Uri.Segments
                    // only works on absolute URIs, so parse the last path segment manually.
                    var loc = resp.Headers.Location?.OriginalString;
                    if (loc is not null)
                    {
                        var lastSlash = loc.LastIndexOf('/');
                        if (lastSlash >= 0 && int.TryParse(loc.AsSpan(lastSlash + 1), out var id))
                        {
                            seededIds.Add(id);
                        }
                    }
                }
            }
        })
        .WithLoadSimulations(Simulation.KeepConstant(copies: 500, during: TimeSpan.FromSeconds(30)));
    }
}
