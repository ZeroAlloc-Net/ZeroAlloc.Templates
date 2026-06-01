using System.Data.Async;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using MyApp.Common;
using ZeroAlloc.Authorization;
using ZeroAlloc.Mediator;
using ZeroAlloc.ORM;
using ZeroAlloc.Results;
using ZeroAlloc.Validation;

namespace MyApp.Features.Customers.CreateCustomer;

#pragma warning disable MA0048 // Vertical-slice convention: request, validator, handler, endpoint, entity co-located.

/// <summary>
/// Create a new customer. ZA.Validation drives <c>CreateCustomerCommandValidator</c>
/// from <c>[Validate]</c> + property attributes; the ZA.Mediator.Validation pipeline
/// behaviour invokes it before the handler. <c>[RequirePolicy("CustomersWrite")]</c>
/// + endpoint-level <c>.RequireAuthorization("CustomersWrite")</c> enforce write
/// scope at both layers.
/// </summary>
[Validate]
[RequirePolicy("CustomersWrite")]
public readonly record struct CreateCustomerCommand(
    [property: NotEmpty] string Name,
    [property: NotEmpty, EmailAddress] string Email)
    : IRequest<Result<CustomerId, Error>>;

public sealed partial class CreateCustomerHandler(IAsyncDbConnection conn)
    : IRequestHandler<CreateCustomerCommand, Result<CustomerId, Error>>
{
    public async ValueTask<Result<CustomerId, Error>> Handle(CreateCustomerCommand cmd, CancellationToken ct)
    {
        var id = await InsertCustomerAsync(cmd.Name, cmd.Email, ct).ConfigureAwait(false);
        return Result<CustomerId, Error>.Success(new CustomerId(id));
    }

    [Command(
        "INSERT INTO \"Customers\" (\"Name\", \"Email\") VALUES (@name, @email) RETURNING \"Id\"",
        Kind = CommandKind.Identity)]
    public partial Task<int> InsertCustomerAsync(string name, string email, CancellationToken ct);
}

public static class CreateCustomerEndpoint
{
    public static void Map(IEndpointRouteBuilder app) =>
        app.MapPost("/customers", static async (CreateCustomerCommand cmd, IMediator mediator, CancellationToken ct) =>
            {
                var result = await mediator.Send(cmd, ct).ConfigureAwait(false);
                return result.IsSuccess
                    ? Results.Created($"/customers/{result.Value.Value}", result.Value)
                    : result.Error.ToProblem();
            })
            .RequireAuthorization("CustomersWrite");
}

/// <summary>
/// Persistence entity owned by this slice. Public so read-side slices
/// (GetCustomer) can project from it; handlers and validators stay internal.
/// Post the ZA.ORM swap, persistence is hand-rolled SQL in the handler — this
/// type is now a plain DTO/domain object; no EF materialisation ctor is needed.
/// </summary>
public sealed class Customer
{
    public Customer(string name, string email)
    {
        Id = new CustomerId(0);
        Name = name;
        Email = email;
    }

    public CustomerId Id { get; private set; }

    public string Name { get; private set; } = string.Empty;

    public string Email { get; private set; } = string.Empty;
}
