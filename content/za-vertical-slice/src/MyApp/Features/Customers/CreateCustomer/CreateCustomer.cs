using System.Data.Async;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.AspNetCore.Routing;
using MyApp.Common;
using ZeroAlloc.Authorization;
using ZeroAlloc.Inject;
using ZeroAlloc.Mediator;
using ZeroAlloc.ORM;
using ZeroAlloc.Results;
using ZeroAlloc.Telemetry;
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

/// <summary>
/// Creates the customer through the slice's <see cref="ICustomerWriter"/> port and returns
/// its newly-assigned <see cref="CustomerId"/>.
/// </summary>
[Scoped]
public sealed class CreateCustomerHandler(ICustomerWriter customers)
    : IRequestHandler<CreateCustomerCommand, Result<CustomerId, Error>>
{
    public async ValueTask<Result<CustomerId, Error>> Handle(CreateCustomerCommand cmd, CancellationToken ct)
    {
        var id = await customers.InsertCustomerAsync(cmd.Name, cmd.Email, ct).ConfigureAwait(false);
        return Result<CustomerId, Error>.Success(new CustomerId(id));
    }
}

/// <summary>
/// The slice's persistence port, and the template's example of ZA.Telemetry. Most slices keep
/// their ZA.ORM partial on the handler; this one puts it behind an interface so it can be
/// instrumented. <c>[Instrument]</c> makes the ZeroAlloc.Telemetry source generator emit
/// <c>CustomerWriterInstrumented</c>, an internal proxy that <c>AddMyApp</c> registers in front
/// of <see cref="CustomerWriter"/>. Every insert then records a <c>customer.insert</c> span, and
/// every successful one adds 1 to the <c>customers.created</c> counter, on the
/// <see cref="Telemetry.SourceName"/> source and meter that Program.cs subscribes to. The
/// proxy is plain generated code: no reflection, Native AOT safe.
/// </summary>
[Instrument(Telemetry.SourceName)]
public interface ICustomerWriter
{
    [Trace("customer.insert")]
    [Count("customers.created")]
    Task<int> InsertCustomerAsync(string name, string email, CancellationToken ct);
}

/// <summary>
/// ZA.ORM implementation of <see cref="ICustomerWriter"/>: the generator emits the
/// <c>[Command]</c> partial's body against the scoped <see cref="IAsyncDbConnection"/>.
/// </summary>
[Scoped]
public sealed partial class CustomerWriter(IAsyncDbConnection conn) : ICustomerWriter
{
    [Command(
        "INSERT INTO \"Customers\" (\"Name\", \"Email\") VALUES (@name, @email) RETURNING \"Id\"",
        Kind = CommandKind.Identity)]
    public partial Task<int> InsertCustomerAsync(string name, string email, CancellationToken ct);
}

public static class CreateCustomerEndpoint
{
    public static void Map(IEndpointRouteBuilder app) =>
        app.MapPost("/customers", static async (CreateCustomerCommand cmd, IMediator mediator, IOutputCacheStore cache, CancellationToken ct) =>
            {
                var result = await mediator.Send(cmd, ct).ConfigureAwait(false);
                if (result.IsSuccess)
                {
                    await cache.EvictByTagAsync("customers", ct).ConfigureAwait(false);
                    return Results.Created($"/customers/{result.Value.Value}", result.Value);
                }

                return result.Error.ToProblem();
            })
            .RequireAuthorization("CustomersWrite");
}

/// <summary>
/// Persistence entity owned by this slice. Public so read-side slices
/// (GetCustomer) can project from it; handlers and validators stay internal.
/// Post the ZA.ORM swap, persistence is hand-rolled SQL in <see cref="CustomerWriter"/> — this
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
