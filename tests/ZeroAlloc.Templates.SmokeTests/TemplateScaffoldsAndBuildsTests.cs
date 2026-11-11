using System.Diagnostics;
using Xunit;

public class TemplateScaffoldsAndBuildsTests
{
    [Theory]
    [InlineData("za-clean")]
    [InlineData("za-vertical-slice")]
    [Trait("Category", "Slow")]
    public async Task Template_installs_scaffolds_builds_and_tests_pass(string templateShortName)
    {
        var repoRoot = FindRepoRoot();
        var templatePath = Path.Combine(repoRoot, "content", templateShortName);
        var scaffoldName = "AcmeCo" + Guid.NewGuid().ToString("N")[..6];
        var work = Path.Combine(Path.GetTempPath(), $"{templateShortName}-smoke-{Guid.NewGuid():N}");
        // An isolated template-engine hive per run: the test runs once per target framework, at the
        // same time, and a shared hive would let one run uninstall the template under the other.
        var hive = Path.Combine(work, "hive");
        var scaffoldDir = Path.Combine(work, "app");

        try
        {
            // 1. Install template from the content directory into the isolated hive.
            await Run("dotnet", $"new install \"{templatePath}\" --debug:custom-hive \"{hive}\"", repoRoot);

            // 2. Scaffold a fresh app
            Directory.CreateDirectory(scaffoldDir);
            await Run("dotnet", $"new {templateShortName} -o \"{scaffoldDir}\" --name {scaffoldName} --debug:custom-hive \"{hive}\"", repoRoot);

            // 2b. The template must ship a .gitignore: the template engine and NuGet can skip
            //     dot-files, so assert it survived install and scaffold.
            Assert.True(File.Exists(Path.Combine(scaffoldDir, ".gitignore")),
                $"Scaffolded {templateShortName} output has no .gitignore");

            // 3. Build the scaffolded app. -warnaserror fails the test on any warning in the generated
            //    code: the template itself ships without TreatWarningsAsErrors.
            await Run("dotnet", $"build \"{Path.Combine(scaffoldDir, $"{scaffoldName}.slnx")}\" -warnaserror --disable-build-servers", scaffoldDir);

            // 4. Test the scaffolded app
            await Run("dotnet", $"test \"{Path.Combine(scaffoldDir, $"{scaffoldName}.slnx")}\" --no-build --disable-build-servers", scaffoldDir);

            // 5. After build and test, a fresh repo must not see any build output as untracked.
            await Run("git", "init -q", scaffoldDir);
            var status = await Capture("git", "status --porcelain --untracked-files=all", scaffoldDir);
            var leaked = status
                .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Where(l => l.Contains("/bin/") || l.Contains("/obj/")
                    || l.Contains("TestResults/") || l.Contains("BenchmarkDotNet.Artifacts/"))
                .ToList();
            Assert.True(leaked.Count == 0,
                $"git status lists build output for {templateShortName}:\n{string.Join('\n', leaked)}");
        }
        finally
        {
            // The hive lives under the work directory, so removing it also uninstalls the template.
            try { if (Directory.Exists(work)) Directory.Delete(work, recursive: true); } catch { }
        }
    }

    private static string FindRepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        // A git worktree has a .git file, not a directory, so test for either.
        while (dir is not null && !Path.Exists(Path.Combine(dir, ".git")))
            dir = Path.GetDirectoryName(dir);
        return dir ?? throw new InvalidOperationException("Repo root not found");
    }

    private static async Task Run(string exe, string args, string workingDir, bool allowFail = false)
        => _ = await Capture(exe, args, workingDir, allowFail);

    private static async Task<string> Capture(string exe, string args, string workingDir, bool allowFail = false)
    {
        var startInfo = new ProcessStartInfo(exe, args)
        {
            WorkingDirectory = workingDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        ChildProcessEnvironment.Apply(startInfo);
        using var p = Process.Start(startInfo) ?? throw new InvalidOperationException($"Failed to start: {exe} {args}");

        var stdout = await p.StandardOutput.ReadToEndAsync();
        var stderr = await p.StandardError.ReadToEndAsync();
        await p.WaitForExitAsync();

        if (p.ExitCode != 0 && !allowFail)
        {
            throw new Xunit.Sdk.XunitException(
                $"Command failed: {exe} {args}\nExit: {p.ExitCode}\nSTDOUT:\n{stdout}\nSTDERR:\n{stderr}");
        }

        return stdout;
    }
}
