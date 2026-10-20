using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Norvia.Docs.Generator.UnitTests;

/// <summary>A small database with each thing the generator documents: an identity key, a default, a cascade and a set null foreign key, a one-to-one, a unique and a filtered index, a check and a sequence.</summary>
internal static class SampleDatabase
{
    public static IModel Model()
    {
        var options = new DbContextOptionsBuilder<SampleContext>().UseSqlServer("Server=unit-test").Options;
        using var context = new SampleContext(options);

        return context.GetService<IDesignTimeModel>().Model;
    }

    private sealed class SampleContext(DbContextOptions<SampleContext> options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.HasSequence<long>("OrderNumbers").StartsAt(1000).IncrementsBy(5);

            modelBuilder.Entity<Customer>(customer =>
            {
                customer.ToTable("Customers");
                customer.Property(c => c.Name).HasMaxLength(200);
                customer.HasIndex(c => c.Name).IsUnique();
            });
            modelBuilder.Entity<Order>(order =>
            {
                order.ToTable("Orders", table => table.HasCheckConstraint("CK_Orders_Total", "[Total] >= 0"));
                order.Property(o => o.Reference).HasMaxLength(30);
                order.Property(o => o.Status).HasMaxLength(20).HasDefaultValueSql("'New'");
                order.Property(o => o.Total).HasColumnType("decimal(18,2)");
                order.HasOne<Customer>().WithMany().HasForeignKey(o => o.CustomerId).OnDelete(DeleteBehavior.Cascade);
                order.HasAlternateKey(o => o.Reference);
            });
            modelBuilder.Entity<OrderNote>(note =>
            {
                note.ToTable("OrderNotes");
                note.HasOne<Order>().WithMany().HasForeignKey(n => n.OrderId).OnDelete(DeleteBehavior.SetNull);
                note.HasIndex(n => n.OrderId).HasFilter("[OrderId] IS NOT NULL");
            });
            modelBuilder.Entity<Receipt>(receipt =>
            {
                receipt.ToTable("Receipts");
                receipt.HasKey(r => r.OrderId);
                receipt.HasOne<Order>().WithOne().HasForeignKey<Receipt>(r => r.OrderId).OnDelete(DeleteBehavior.Restrict);
            });
        }
    }

    private sealed class Customer
    {
        public long Id { get; set; }

        public required string Name { get; set; }

        public string? Notes { get; set; }
    }

    private sealed class Order
    {
        public int Id { get; set; }

        public long CustomerId { get; set; }

        public required string Reference { get; set; }

        public required string Status { get; set; }

        public decimal Total { get; set; }
    }

    private sealed class OrderNote
    {
        public int Id { get; set; }

        public int? OrderId { get; set; }
    }

    private sealed class Receipt
    {
        public int OrderId { get; set; }
    }
}
