using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using MyApp.Domain;
using MyApp.Domain.ValueObjects;

namespace MyApp.Infrastructure.Persistence.Configurations;

public sealed class OrderConfiguration : IEntityTypeConfiguration<Order>
{
    // Money is a readonly struct value-object with a private constructor and a
    // TryCreate factory. EF Core 9's OwnedNavigationBuilder does not expose
    // ComplexProperty inside an OwnsMany configuration, so we serialise Money
    // round-trips through a value-converter as "<amount>|<currency>" inside
    // OrderLine and use ComplexProperty for the top-level Order.Total.
    private static readonly ValueConverter<Money, string> s_moneyConverter = new(
        m => m.Amount.ToString(System.Globalization.CultureInfo.InvariantCulture) + "|" + m.Currency,
        s => FromString(s));

    private static Money FromString(string s)
    {
        var pipe = s.IndexOf('|');
        var amount = decimal.Parse(s.AsSpan(0, pipe), System.Globalization.CultureInfo.InvariantCulture);
        var currency = s[(pipe + 1)..];
        return Money.TryCreate(amount, currency).Value;
    }

    public void Configure(EntityTypeBuilder<Order> builder)
    {
        builder.ToTable("Orders");

        builder.HasKey(o => o.Id);

        builder.Property(o => o.Id)
            .HasConversion(id => id.Value, v => new OrderId(v))
            .ValueGeneratedOnAdd();

        builder.Property(o => o.CustomerId)
            .HasConversion(id => id.Value, v => new CustomerId(v))
            .IsRequired();

        builder.Property(o => o.Status)
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();

        // Money is a readonly struct, so it cannot be modelled with OwnsOne
        // (which requires a reference type). EF 9's ComplexProperty handles
        // structs at the entity level cleanly.
        builder.ComplexProperty(o => o.Total, total =>
        {
            total.Property(m => m.Amount).HasColumnName("TotalAmount").IsRequired();
            total.Property(m => m.Currency).HasColumnName("TotalCurrency").HasMaxLength(3).IsRequired();
        });

        // OrderLine is a sealed record (reference type); map it as an owned
        // collection with a shadow PK so the navigation behaves like part of
        // the Order aggregate. The Lines navigation is backed by the private
        // _lines field configured below.
        builder.OwnsMany<OrderLine>("Lines", line =>
        {
            line.ToTable("OrderLines");
            line.WithOwner().HasForeignKey("OrderId");
            line.Property<int>("Id").ValueGeneratedOnAdd();
            line.HasKey("Id");
            line.Property(l => l.Sku).HasMaxLength(64).IsRequired();
            line.Property(l => l.Quantity).IsRequired();
            line.Property(l => l.Price)
                .HasConversion(s_moneyConverter)
                .HasColumnName("Price")
                .IsRequired();
        });

        builder.Navigation("Lines")
            .UsePropertyAccessMode(PropertyAccessMode.Field)
            .HasField("_lines");
    }
}
