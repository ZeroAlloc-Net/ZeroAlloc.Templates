using BenchmarkDotNet.Running;

namespace MyApp.Benchmarks.Primitives;

internal static class Entry
{
    public static int Main(string[] args)
    {
        var summaries = BenchmarkSwitcher.FromAssembly(typeof(Entry).Assembly).Run(args);
        return summaries.Any(s => s.HasCriticalValidationErrors) ? 1 : 0;
    }
}
