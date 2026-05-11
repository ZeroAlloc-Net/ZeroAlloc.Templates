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

    // Must be at least internal — the EF Core compiled model generator emits
    // code that calls this from a sibling namespace.
    internal static Money FromString(string s)
    {
        // EF Core probes the converter with the property's sentinel value (the
        // default string, i.e. empty) during model initialization to compute
        // a sentinel for the converted CLR type. Return a zero Money for that
        // case rather than throwing.
        if (string.IsNullOrEmpty(s))
        {
            return Money.TryCreate(0m, "USD").Value;
        }
        var pipe = s.IndexOf('|');
        if (pipe < 0)
        {
            return Money.TryCreate(0m, "USD").Value;
        }
        var amountSpan = s.AsSpan(0, pipe);
        var currency = s[(pipe + 1)..];
        if (amountSpan.IsEmpty || string.IsNullOrEmpty(currency)
            || !decimal.TryParse(amountSpan, System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out var amount)
            || amount < 0)
        {
            return Money.TryCreate(0m, "USD").Value;
        }
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

        // Money is a readonly struct; EF Core 10's NativeAOT compiled-model
        // generator emits UnsafeAccessor field accessors that pass by value
        // for readonly structs, which fails verification at runtime. We
        // route Money through the same string converter used by OrderLine
        // (no field access required — only public property reads).
        builder.Property(o => o.Total)
            .HasConversion(s_moneyConverter)
            .HasColumnName("Total")
            .IsRequired();

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
