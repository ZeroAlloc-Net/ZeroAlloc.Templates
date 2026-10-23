using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using MyApp.Common;
using Xunit;

namespace MyApp.UnitTests.Common;

/// <summary>Which HTTP status each kind of slice failure reaches the client as.</summary>
public sealed class ErrorTests
{
    public static TheoryData<Error, int> Errors => new()
    {
        { Error.ValidationFailed("order.invalid", "bad"), StatusCodes.Status400BadRequest },
        { Error.Unauthorized("order.unauthorized", "who"), StatusCodes.Status401Unauthorized },
        { Error.NotFound("order.not_found", "gone"), StatusCodes.Status404NotFound },
        { Error.Conflict("order.conflict", "again"), StatusCodes.Status409Conflict },
        { Error.Failure("order.failed", "broke"), StatusCodes.Status500InternalServerError },
    };

    [Theory]
    [MemberData(nameof(Errors))]
    public void ToProblem_maps_each_kind_to_its_status_and_keeps_code_and_message(Error error, int status)
    {
        var problem = Assert.IsType<ProblemHttpResult>(error.ToProblem());

        Assert.Equal(status, problem.StatusCode);
        Assert.Equal(error.Code, problem.ProblemDetails.Title);
        Assert.Equal(error.Message, problem.ProblemDetails.Detail);
    }

    [Fact]
    public void A_validation_error_becomes_a_400_naming_the_field()
    {
        var problem = Assert.IsType<ProblemHttpResult>(new ValidationError("Total", "must be positive").ToProblem());

        Assert.Equal(StatusCodes.Status400BadRequest, problem.StatusCode);
        Assert.Equal("validation.failed", problem.ProblemDetails.Title);
        Assert.Equal("Total: must be positive", problem.ProblemDetails.Detail);
    }
}
