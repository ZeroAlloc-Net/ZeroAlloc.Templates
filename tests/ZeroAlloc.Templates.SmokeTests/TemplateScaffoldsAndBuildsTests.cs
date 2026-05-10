using System.Diagnostics;
using Xunit;

public class TemplateScaffoldsAndBuildsTests
{
    [Fact]
    [Trait("Category", "Slow")]
    public async Task Template_installs_scaffolds_builds_and_tests_pass()
    {
        var repoRoot = FindRepoRoot();
        var templatePath = Path.Combine(repoRoot, "content", "za-clean");
        var scaffoldName = "AcmeCo" + Guid.NewGuid().ToString("N")[..6];
        var scaffoldDir = Path.Combine(Path.GetTempPath(), $"za-clean-smoke-{Guid.NewGuid():N}");

        try
        {
            // 1. Install template from the content directory (--force is idempotent: reinstalls
            //    if already installed, installs fresh otherwise).
            await Run("dotnet", $"new install \"{templatePath}\" --force", repoRoot);

            // 2. Scaffold a fresh app
            Directory.CreateDirectory(scaffoldDir);
            await Run("dotnet", $"new za-clean -o \"{scaffoldDir}\" --name {scaffoldName}", repoRoot);

            // 3. Build the scaffolded app
            await Run("dotnet", $"build \"{Path.Combine(scaffoldDir, $"{scaffoldName}.slnx")}\"", scaffoldDir);

            // 4. Test the scaffolded app
            await Run("dotnet", $"test \"{Path.Combine(scaffoldDir, $"{scaffoldName}.slnx")}\" --no-build", scaffoldDir);
        }
        finally
        {
            // Cleanup (uninstall by path, since the template was installed from a folder)
            try { await Run("dotnet", $"new uninstall \"{templatePath}\"", repoRoot, allowFail: true); } catch { }
            try { if (Directory.Exists(scaffoldDir)) Directory.Delete(scaffoldDir, recursive: true); } catch { }
        }
    }

    private static string FindRepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null && !Directory.Exists(Path.Combine(dir, ".git")))
            dir = Path.GetDirectoryName(dir);
        return dir ?? throw new InvalidOperationException("Repo root not found");
    }

    private static async Task Run(string exe, string args, string workingDir, bool allowFail = false)
    {
        using var p = Process.Start(new ProcessStartInfo(exe, args)
        {
            WorkingDirectory = workingDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        }) ?? throw new InvalidOperationException($"Failed to start: {exe} {args}");

        var stdout = await p.StandardOutput.ReadToEndAsync();
        var stderr = await p.StandardError.ReadToEndAsync();
        await p.WaitForExitAsync();

        if (p.ExitCode != 0 && !allowFail)
        {
            throw new Xunit.Sdk.XunitException(
                $"Command failed: {exe} {args}\nExit: {p.ExitCode}\nSTDOUT:\n{stdout}\nSTDERR:\n{stderr}");
        }
    }
}
