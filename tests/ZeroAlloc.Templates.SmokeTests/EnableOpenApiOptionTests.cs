using System.Diagnostics;
using System.Text.RegularExpressions;
using Xunit;

/// <summary>
/// Per-option smoke tests for <c>EnableOpenApi</c>: on (default) the scaffold wires the built-in
/// OpenAPI document and Scalar and still builds; off, neither package is referenced and no
/// OpenAPI code remains. Uses an isolated template-engine hive so it never races the install and
/// uninstall done by the other smoke tests, which run in parallel.
/// </summary>
public class EnableOpenApiOptionTests
{
    private static readonly string[] Templates = ["za-clean", "za-vertical-slice"];

    public static TheoryData<string, bool> Cases()
    {
        var data = new TheoryData<string, bool>();
        foreach (var t in Templates)
        {
            data.Add(t, true);
            data.Add(t, false);
        }
        return data;
    }

    [Theory]
    [InlineData("za-clean")]
    [InlineData("za-vertical-slice")]
    public void Template_declares_only_the_EnableOpenApi_symbol(string template)
    {
        var json = File.ReadAllText(Path.Combine(
            FindRepoRoot(), "content", template, ".template.config", "template.json"));

        Assert.Contains("\"EnableOpenApi\"", json);
        Assert.DoesNotContain("EnableSwagger", json);
        Assert.DoesNotContain("SeedDatabase", json);
        Assert.DoesNotContain("IncludeDocker", json);
    }

    [Theory]
    [MemberData(nameof(Cases))]
    [Trait("Category", "Slow")]
    public async Task EnableOpenApi_controls_the_openapi_wiring_and_the_scaffold_builds(string template, bool enabled)
    {
        var repoRoot = FindRepoRoot();
        var templatePath = Path.Combine(repoRoot, "content", template);
        var name = "AcmeCo" + Guid.NewGuid().ToString("N")[..6];
        var work = Path.Combine(Path.GetTempPath(), $"{template}-openapi-{Guid.NewGuid():N}");
        var hive = Path.Combine(work, "hive");
        var scaffoldDir = Path.Combine(work, "app");

        try
        {
            Directory.CreateDirectory(scaffoldDir);
            await Run($"new install \"{templatePath}\" --debug:custom-hive \"{hive}\"", repoRoot);
            await Run($"new {template} -o \"{scaffoldDir}\" --name {name} --EnableOpenApi {(enabled ? "true" : "false")} " +
                      $"--debug:custom-hive \"{hive}\"", repoRoot);

            var sources = Directory.EnumerateFiles(scaffoldDir, "*.*", SearchOption.AllDirectories)
                .Where(f => f.EndsWith(".cs") || f.EndsWith(".csproj"))
                .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
                .ToDictionary(f => f, File.ReadAllText);

            var program = Assert.Single(sources, kv => Path.GetFileName(kv.Key) == "Program.cs"
                && kv.Key.Contains($"{Path.DirectorySeparatorChar}src{Path.DirectorySeparatorChar}")).Value;
            var all = string.Join("\n", sources.Values);

            Assert.DoesNotContain("EnableSwagger", all);
            Assert.DoesNotContain("AddEndpointsApiExplorer", all);

            if (enabled)
            {
                Assert.Contains("builder.Services.AddOpenApi();", program);
                Assert.Contains("app.MapOpenApi();", program);
                Assert.Contains("app.MapScalarApiReference();", program);
                Assert.Matches(new Regex(@"IsDevelopment\(\)\s*\)\s*\{[^}]*MapOpenApi", RegexOptions.Singleline), program);
                Assert.Contains("Microsoft.AspNetCore.OpenApi", all);
                Assert.Contains("Scalar.AspNetCore", all);
            }
            else
            {
                Assert.DoesNotContain("OpenApi", all);
                Assert.DoesNotContain("Scalar.AspNetCore", all);
                Assert.DoesNotContain("MapScalarApiReference", all);
            }

            await Run($"build \"{Path.Combine(scaffoldDir, $"{name}.slnx")}\" -warnaserror", scaffoldDir);
        }
        finally
        {
            try { if (Directory.Exists(work)) Directory.Delete(work, recursive: true); } catch { }
        }
    }

    private static string FindRepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null && !Path.Exists(Path.Combine(dir, ".git")))
            dir = Path.GetDirectoryName(dir);
        return dir ?? throw new InvalidOperationException("Repo root not found");
    }

    private static async Task Run(string args, string workingDir)
    {
        using var p = Process.Start(new ProcessStartInfo("dotnet", args)
        {
            WorkingDirectory = workingDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        }) ?? throw new InvalidOperationException($"Failed to start: dotnet {args}");

        var stdoutTask = p.StandardOutput.ReadToEndAsync();
        var stderrTask = p.StandardError.ReadToEndAsync();
        await p.WaitForExitAsync();
        var stdout = await stdoutTask;
        var stderr = await stderrTask;

        if (p.ExitCode != 0)
        {
            throw new Xunit.Sdk.XunitException(
                $"Command failed: dotnet {args}\nExit: {p.ExitCode}\nSTDOUT:\n{stdout}\nSTDERR:\n{stderr}");
        }
    }
}
