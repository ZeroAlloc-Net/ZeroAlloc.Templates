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
