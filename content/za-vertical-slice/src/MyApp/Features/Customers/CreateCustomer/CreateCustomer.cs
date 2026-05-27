using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using MyApp.Common;
using MyApp.Persistence;
using ZeroAlloc.Authorization;
using ZeroAlloc.Mediator;
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

public sealed class CreateCustomerHandler(AppDbContext db)
    : IRequestHandler<CreateCustomerCommand, Result<CustomerId, Error>>
{
    public async ValueTask<Result<CustomerId, Error>> Handle(CreateCustomerCommand cmd, CancellationToken ct)
    {
        var customer = new Customer(cmd.Name, cmd.Email);
        await db.Customers.AddAsync(customer, ct).ConfigureAwait(false);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Result<CustomerId, Error>.Success(customer.Id);
    }
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
/// Persistence entity owned by this slice. EF Core assigns <see cref="Id"/> on
/// INSERT via the <see cref="CustomerId"/> value-converter configured in
/// <see cref="AppDbContext.OnModelCreating"/>. Public so read-side slices
/// (GetCustomer) can project from it; handlers and validators stay internal.
/// </summary>
public sealed class Customer
{
    private Customer()
    {
    }

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
