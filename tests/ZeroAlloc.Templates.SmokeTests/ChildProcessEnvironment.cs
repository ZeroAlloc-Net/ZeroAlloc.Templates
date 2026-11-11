using System.Diagnostics;

/// <summary>
/// Environment for the <c>dotnet</c> child processes the smoke tests start.
/// </summary>
/// <remarks>
/// <c>dotnet test</c> exports the MSBuild location of the SDK it runs on (<c>MSBuildSDKsPath</c>,
/// <c>MSBuildExtensionsPath</c>, <c>DOTNET_HOST_PATH</c> and others). The repo root pins SDK 11 but a
/// scaffolded app pins SDK 10 in its own global.json, so a child that inherits those variables
/// mixes the two SDKs: its build fails with MSB4216 and the orphaned MSBuild task hosts keep the
/// redirected output pipes open, which hangs the test for good. Each child must pick its SDK from
/// its own global.json, so the variables are removed.
/// </remarks>
internal static class ChildProcessEnvironment
{
    public static void Apply(ProcessStartInfo startInfo)
    {
        foreach (var name in startInfo.Environment.Keys.ToList())
        {
            if (name.StartsWith("MSBUILD", StringComparison.OrdinalIgnoreCase)
                || name.StartsWith("VSTEST_", StringComparison.Ordinal)
                || name == "DOTNET_HOST_PATH")
            {
                startInfo.Environment.Remove(name);
            }
        }
    }
}
