using BenchmarkDotNet.Running;

namespace MyApp.Benchmarks;

internal static class BenchmarksEntryPoint
{
    public static int Main(string[] args)
    {
        var summaries = BenchmarkSwitcher.FromAssembly(typeof(BenchmarksEntryPoint).Assembly).Run(args);
        return summaries.Any(s => s.HasCriticalValidationErrors) ? 1 : 0;
    }
}
