using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using MyApp.Common;
using MyApp.Features.Customers.CreateCustomer;
using MyApp.Persistence;
using ZeroAlloc.Authorization;
using ZeroAlloc.Mediator;
using ZeroAlloc.Results;
using ZeroAlloc.Validation;

namespace MyApp.Features.Customers.GetCustomer;

#pragma warning disable MA0048 // Vertical-slice convention: request, DTO, handler, endpoint co-located.

/// <summary>
/// Retrieve a single customer by id. Mirrors GetOrder's shape: filter by the
/// value-converted typed id, project to a slice-local DTO, surface NotFound
/// as <see cref="Error.NotFound"/>.
/// </summary>
[Validate]
[RequirePolicy("CustomersRead")]
public readonly record struct GetCustomerQuery([property: GreaterThan(0)] int Id)
    : IRequest<Result<CustomerDto, Error>>;

public sealed record CustomerDto(int Id, string Name, string Email);

public sealed class GetCustomerHandler(AppDbContext db)
    : IRequestHandler<GetCustomerQuery, Result<CustomerDto, Error>>
{
    public async ValueTask<Result<CustomerDto, Error>> Handle(GetCustomerQuery query, CancellationToken ct)
    {
        var customer = await db.Customers
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == new CustomerId(query.Id), ct)
            .ConfigureAwait(false);

        return customer is null
            ? Result<CustomerDto, Error>.Failure(Error.NotFound(
                "customer.not_found",
                $"Customer {query.Id} not found"))
            : Result<CustomerDto, Error>.Success(new CustomerDto(
                customer.Id.Value,
                customer.Name,
                customer.Email));
    }
}

public static class GetCustomerEndpoint
{
    public static void Map(IEndpointRouteBuilder app) =>
        app.MapGet("/customers/{id:int}", static async (int id, IMediator mediator, CancellationToken ct) =>
            {
                var result = await mediator.Send(new GetCustomerQuery(id), ct).ConfigureAwait(false);
                return result.IsSuccess
                    ? Results.Ok(result.Value)
                    : result.Error.ToProblem();
            })
            .RequireAuthorization("CustomersRead");
}
