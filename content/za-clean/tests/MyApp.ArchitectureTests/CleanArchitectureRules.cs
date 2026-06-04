using System.Reflection;
using MyApp.Application.CreateOrder;
using MyApp.Domain;
using MyApp.Infrastructure.Persistence;
using NetArchTest.Rules;
using Xunit;
using ZeroAlloc.Mediator;

namespace MyApp.ArchitectureTests;

public class CleanArchitectureRules
{
    private static readonly Assembly Domain = typeof(Order).Assembly;
    private static readonly Assembly Application = typeof(CreateOrderCommand).Assembly;
    private static readonly Assembly Infrastructure = typeof(OrderRepository).Assembly;
    private static readonly Assembly Api = typeof(Program).Assembly;

    [Fact]
    public void Domain_does_not_depend_on_anything_outside_Domain()
    {
        var result = Types.InAssembly(Domain)
            .Should()
            .NotHaveDependencyOnAny(
                "MyApp.Application", "MyApp.Infrastructure", "MyApp.Api",
                "Microsoft.EntityFrameworkCore", "Microsoft.AspNetCore")
            .GetResult();
        Assert.True(result.IsSuccessful, FormatFailure(result));
    }

    [Fact]
    public void Application_does_not_depend_on_Infrastructure_or_Api()
    {
        var result = Types.InAssembly(Application)
            .Should()
            .NotHaveDependencyOnAny(
                "MyApp.Infrastructure", "MyApp.Api",
                "Microsoft.EntityFrameworkCore", "Microsoft.AspNetCore")
            .GetResult();
        Assert.True(result.IsSuccessful, FormatFailure(result));
    }

    [Fact]
    public void Infrastructure_does_not_depend_on_Api()
    {
        var result = Types.InAssembly(Infrastructure)
            .Should()
            .NotHaveDependencyOnAny("MyApp.Api")
            .GetResult();
        Assert.True(result.IsSuccessful, FormatFailure(result));
    }

    [Fact]
    public void Handlers_live_in_Application_only()
    {
        var offenders = Types.InAssemblies(new[] { Domain, Infrastructure, Api })
            .That()
            .ImplementInterface(typeof(IRequestHandler<,>))
            .GetTypes()
            .Select(t => t.FullName)
            .ToArray();
        Assert.True(
            offenders.Length == 0,
            $"IRequestHandler implementations found outside MyApp.Application:\n  - {string.Join("\n  - ", offenders)}");
    }

    [Fact]
    public void Query_handlers_return_application_query_models_not_domain_entities()
    {
        var handlerInterface = typeof(IRequestHandler<,>);
        var domainAssembly = Domain;

        var offenders = Types.InAssembly(Application)
            .That()
            .ImplementInterface(handlerInterface)
            .GetTypes()
            .SelectMany(t => t.GetInterfaces())
            .Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == handlerInterface)
            .Select(i => new { Handler = i, Result = i.GenericTypeArguments[1] })
            .Where(x =>
            {
                var result = x.Result;
                if (result.IsGenericType &&
                    result.GetGenericTypeDefinition().Name.StartsWith("Result", StringComparison.Ordinal))
                {
                    var inner = result.GenericTypeArguments[0];
                    return IsForbiddenDomainEntity(inner, domainAssembly);
                }
                return IsForbiddenDomainEntity(result, domainAssembly);
            })
            .Select(x => x.Handler.GenericTypeArguments[0].FullName)
            .ToArray();

        Assert.True(
            offenders.Length == 0,
            $"Query/command handlers returning Domain entities (not value objects):\n  - {string.Join("\n  - ", offenders)}");

        static bool IsForbiddenDomainEntity(Type t, Assembly domain)
        {
            if (t.Assembly != domain) return false;
            return !(t.Namespace?.EndsWith(".ValueObjects", StringComparison.Ordinal) ?? false);
        }
    }

    private static string FormatFailure(TestResult r) =>
        $"Violating types:\n  - {string.Join("\n  - ", r.FailingTypeNames ?? Enumerable.Empty<string>())}";
}
