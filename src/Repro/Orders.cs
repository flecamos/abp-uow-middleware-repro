using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Volo.Abp.AspNetCore.Mvc;
using Volo.Abp.Data;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Entities;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.EntityFrameworkCore;
using Volo.Abp.EventBus;
using Volo.Abp.EventBus.Local;

namespace Repro;

public class Customer : Entity<int>
{
    protected Customer() { }
}

public class Order : Entity<Guid>
{
    public int CustomerId { get; private set; }

    protected Order() { }

    public Order(Guid id, int customerId)
        : base(id)
    {
        CustomerId = customerId;
    }
}

public record OrderPlaced(Guid OrderId);

public record PlaceOrderInput(int CustomerId);

public record OrderCreated(Guid Id);

[ConnectionStringName("Default")]
public class ReproDbContext(DbContextOptions<ReproDbContext> options) : AbpDbContext<ReproDbContext>(options)
{
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<Order> Orders => Set<Order>();
}

[Route("api/orders")]
public class OrdersController(IRepository<Order, Guid> orders, ILocalEventBus localEventBus) : AbpControllerBase
{
    [HttpPost]
    public async Task<OrderCreated> PlaceAsync([FromBody] PlaceOrderInput input)
    {
        var order = await orders.InsertAsync(new Order(GuidGenerator.Create(), input.CustomerId));
        return new OrderCreated(order.Id);
    }

    [HttpPost("with-event")]
    public async Task<OrderCreated> PlaceWithEventAsync([FromBody] PlaceOrderInput input)
    {
        var order = await orders.InsertAsync(new Order(GuidGenerator.Create(), input.CustomerId));
        await localEventBus.PublishAsync(new OrderPlaced(order.Id));
        return new OrderCreated(order.Id);
    }

    [HttpGet("count")]
    public Task<long> CountAsync() => orders.GetCountAsync();
}

public class FailingOrderPlacedHandler : ILocalEventHandler<OrderPlaced>, ITransientDependency
{
    public Task HandleEventAsync(OrderPlaced eventData) =>
        throw new InvalidOperationException("The handler failed, so the order must not be saved.");
}
