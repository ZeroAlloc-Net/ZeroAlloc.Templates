using MyApp.Infrastructure.Persistence;
using Npgsql;
using Xunit;

namespace MyApp.IntegrationTests;

public sealed class NpgsqlConnectionStringsTests
{
    [Fact]
    public void Turns_on_auto_prepare_and_multiplexing_when_the_connection_string_does_not_set_them()
    {
        var result = new NpgsqlConnectionStringBuilder(
            NpgsqlConnectionStrings.WithThroughputDefaults("Host=db;Database=app;Username=u"));

        Assert.Equal(20, result.MaxAutoPrepare);
        Assert.True(result.Multiplexing);
        Assert.Equal("db", result.Host);
        Assert.Equal("app", result.Database);
    }

    [Fact]
    public void Keeps_values_the_connection_string_sets_even_when_they_are_the_npgsql_defaults()
    {
        var result = new NpgsqlConnectionStringBuilder(
            NpgsqlConnectionStrings.WithThroughputDefaults("Host=db;Multiplexing=false;Max Auto Prepare=0"));

        Assert.Equal(0, result.MaxAutoPrepare);
        Assert.False(result.Multiplexing);
    }

    [Fact]
    public void Treats_a_keyword_synonym_as_set()
    {
        var result = new NpgsqlConnectionStringBuilder(
            NpgsqlConnectionStrings.WithThroughputDefaults("Host=db;MaxAutoPrepare=5"));

        Assert.Equal(5, result.MaxAutoPrepare);
        Assert.True(result.Multiplexing);
    }
}
