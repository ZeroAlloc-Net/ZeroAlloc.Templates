using MyApp.Authorization;
using Xunit;
using ZeroAlloc.Authorization;

namespace MyApp.UnitTests.Authorization;

public sealed class ScopePolicyTests
{
    [Theory]
    [InlineData("orders.read", true)]
    [InlineData("orders.write", true)]
    [InlineData("profile orders.write", true)]
    [InlineData("orders.reader orders", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public async Task OrdersRead_accepts_either_order_scope_as_a_whole_word(string? scope, bool allowed)
    {
        var result = await new OrdersReadPolicy().EvaluateAsync(new Context(scope));

        Assert.Equal(allowed, result.IsSuccess);
        if (!allowed)
            Assert.Equal(AuthorizationFailure.DefaultDenyCode, result.Error.Code);
    }

    [Theory]
    [InlineData("orders.write", true)]
    [InlineData("orders.read", false)]
    [InlineData("orders.write.all", false)]
    [InlineData(null, false)]
    public async Task OrdersWrite_requires_the_write_scope(string? scope, bool allowed)
    {
        var result = await new OrdersWritePolicy().EvaluateAsync(new Context(scope));

        Assert.Equal(allowed, result.IsSuccess);
    }

    [Theory]
    [InlineData("customers.read", true)]
    [InlineData("customers.write", true)]
    [InlineData("orders.read", false)]
    public async Task CustomersRead_accepts_either_customer_scope(string scope, bool allowed)
    {
        var result = await new CustomersReadPolicy().EvaluateAsync(new Context(scope));

        Assert.Equal(allowed, result.IsSuccess);
    }

    private sealed class Context(string? scope) : ISecurityContext
    {
        public string Id => "test";

        public IReadOnlySet<string> Roles { get; } = new HashSet<string>(StringComparer.Ordinal);

        public IReadOnlyDictionary<string, string> Claims { get; } = scope is null
            ? new Dictionary<string, string>(StringComparer.Ordinal)
            : new Dictionary<string, string>(StringComparer.Ordinal) { ["scope"] = scope };
    }
}
