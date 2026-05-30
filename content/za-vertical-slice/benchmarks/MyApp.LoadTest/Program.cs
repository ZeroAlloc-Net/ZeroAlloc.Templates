// NBomber load test for the PlaceOrder hot path.
//
// Local recipe — run against Postgres (Sqlite's single-process file lock
// caps the SUT at ~470 RPS, not a meaningful production signal):
//
//   docker run --rm -d -p 5432:5432 \
//     -e POSTGRES_PASSWORD=postgres -e POSTGRES_DB=myapp_load \
//     --name myapp-load-pg postgres:17 \
//     -c max_connections=500
//
//   Database__Provider=Postgres \
//   ConnectionStrings__Default="Host=localhost;Port=5432;Username=postgres;Password=postgres;Database=myapp_load;Maximum Pool Size=500" \
//   dotnet run -c Release --project src/MyApp &
//
//   until curl -fs http://localhost:5000/healthz; do sleep 0.5; done
//   dotnet run -c Release --project benchmarks/MyApp.LoadTest
//
//   kill %1; docker stop myapp-load-pg
//
// CI: the `nbomber-postgres-vs` job in .github/workflows/benchmarks.yml
// runs this end-to-end on every manual workflow trigger and uploads the
// NBomber report as the `nbomber-za-vertical-slice-postgres` artifact.

using MyApp.LoadTest;
using NBomber.CSharp;

var baseUrl = args.Length > 0 ? args[0] : "http://localhost:5000";
var token = TestJwt.Issue(["orders.read", "orders.write"]);

// Open-model load is tunable without recompiling so the same scenario can probe
// for the throughput ceiling on different hardware:
//   LOADTEST_TARGET_RPS  — sustained requests/sec to inject (default 10000)
//   LOADTEST_DURATION_S  — sustain duration in seconds (default 30)
var targetRps = EnvInt("LOADTEST_TARGET_RPS", 10_000);
var durationSeconds = EnvInt("LOADTEST_DURATION_S", 30);

var scenario = ReadHotPathScenario.Build(baseUrl, token, targetRps, durationSeconds);

NBomberRunner
    .RegisterScenarios(scenario)
    .Run();

static int EnvInt(string name, int fallback) =>
    int.TryParse(Environment.GetEnvironmentVariable(name), out var v) && v > 0 ? v : fallback;
