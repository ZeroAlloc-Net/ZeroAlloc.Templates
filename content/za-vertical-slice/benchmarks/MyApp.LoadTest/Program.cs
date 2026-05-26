using MyApp.LoadTest;
using NBomber.CSharp;

var baseUrl = args.Length > 0 ? args[0] : "http://localhost:5000";
var token = TestJwt.Issue(["orders.read", "orders.write"]);

var scenario = ReadHotPathScenario.Build(baseUrl, token);

NBomberRunner
    .RegisterScenarios(scenario)
    .Run();
