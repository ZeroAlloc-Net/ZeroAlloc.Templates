using System.Reflection;
using NetArchTest.Rules;
using Xunit;
using ZeroAlloc.Mediator;

namespace MyApp.ConventionTests;

/// <summary>
/// Convention rules for the vertical-slice template. NetArchTest asserts the
/// structural shape every slice must follow so regressions land as red tests
/// rather than as merged code. Rules are intentionally narrow:
/// <list type="bullet">
///   <item><description><see cref="Every_command_or_query_implements_IRequest"/></description></item>
///   <item><description><see cref="Every_handler_is_sealed_and_public"/></description></item>
///   <item><description><see cref="Every_handler_implements_IRequestHandler"/></description></item>
///   <item><description><see cref="Every_endpoint_is_a_public_static_class"/></description></item>
/// </list>
/// <para>
/// Notably <i>not</i> enforced: "no slice references another slice's internals."
/// Entity types intentionally cross slice boundaries (Order is owned by
/// PlaceOrder but read by GetOrder / ListOrders / CancelOrder; same for
/// Customer). Handlers, validators, and DTOs naturally stay slice-local via
/// the namespace structure — adding a cross-slice reference shows up as a
/// new <c>using</c> at code-review time, which is the cheaper enforcement
/// surface than a runtime rule.
/// </para>
/// </summary>
public sealed class VerticalSliceConventionRules
{
    private static readonly Assembly App = typeof(Program).Assembly;

    // Two suffixes are checked separately because NetArchTest's Or() doesn't
    // parenthesize the preceding And() chain — combining the namespace filter
    // with `HaveNameEndingWith("Command").Or().HaveNameEndingWith("Query")`
    // matches "*Query" anywhere in the assembly, not just inside Features.
    [Theory]
    [InlineData("Command")]
    [InlineData("Query")]
    public void Every_request_type_implements_IRequest(string suffix)
    {
        var result = Types.InAssembly(App)
            .That()
            .ResideInNamespaceStartingWith("MyApp.Features.")
            .And()
            .HaveNameEndingWith(suffix)
            .And()
            .DoNotHaveNameEndingWith("Validator")
            .Should()
            .ImplementInterface(typeof(IRequest<>))
            .GetResult();
        Assert.True(result.IsSuccessful, FormatFailure(result));
    }

    [Fact]
    public void Every_handler_is_sealed_and_public()
    {
        var result = Types.InAssembly(App)
            .That()
            .ResideInNamespaceStartingWith("MyApp.Features.")
            .And()
            .HaveNameEndingWith("Handler")
            .Should()
            .BeSealed()
            .And()
            .BePublic()
            .GetResult();
        Assert.True(result.IsSuccessful, FormatFailure(result));
    }

    [Fact]
    public void Every_handler_implements_IRequestHandler()
    {
        var result = Types.InAssembly(App)
            .That()
            .ResideInNamespaceStartingWith("MyApp.Features.")
            .And()
            .HaveNameEndingWith("Handler")
            .Should()
            .ImplementInterface(typeof(IRequestHandler<,>))
            .GetResult();
        Assert.True(result.IsSuccessful, FormatFailure(result));
    }

    [Fact]
    public void Every_endpoint_is_a_public_static_class()
    {
        // A C# `static class` is encoded as `abstract sealed` in IL. NetArchTest
        // doesn't expose a direct "IsStatic" predicate, but BeAbstract() + BeSealed()
        // is the canonical encoding.
        var result = Types.InAssembly(App)
            .That()
            .ResideInNamespaceStartingWith("MyApp.Features.")
            .And()
            .HaveNameEndingWith("Endpoint")
            .Should()
            .BePublic()
            .And()
            .BeAbstract()
            .And()
            .BeSealed()
            .GetResult();
        Assert.True(result.IsSuccessful, FormatFailure(result));
    }

    private static string FormatFailure(TestResult r) =>
        $"Violating types:\n  - {string.Join("\n  - ", r.FailingTypeNames ?? Enumerable.Empty<string>())}";
}
