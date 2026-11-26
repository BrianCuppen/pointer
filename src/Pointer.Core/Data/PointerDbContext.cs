using Microsoft.EntityFrameworkCore;
using Pointer.Core.Domain;

namespace Pointer.Core.Data;

public class PointerDbContext(DbContextOptions<PointerDbContext> options) : DbContext(options)
{
    /// <summary>Name of the exclusion constraint that prevents overlapping bookings per staff member.</summary>
    public const string BookingOverlapConstraint = "ex_bookings_no_overlap";

    public DbSet<Shop> Shops => Set<Shop>();
    public DbSet<Service> Services => Set<Service>();
    public DbSet<Staff> Staff => Set<Staff>();
    public DbSet<OpeningHours> OpeningHours => Set<OpeningHours>();
    public DbSet<TimeOff> TimeOff => Set<TimeOff>();
    public DbSet<Booking> Bookings => Set<Booking>();
    public DbSet<Payment> Payments => Set<Payment>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.Properties<NoShowPolicy>().HaveConversion<SnakeCaseEnumConverter<NoShowPolicy>>().HaveMaxLength(32);
        configurationBuilder.Properties<BookingStatus>().HaveConversion<SnakeCaseEnumConverter<BookingStatus>>().HaveMaxLength(32);
        configurationBuilder.Properties<PaymentStatus>().HaveConversion<SnakeCaseEnumConverter<PaymentStatus>>().HaveMaxLength(32);
    }

    protected override void OnModelCreating(ModelBuilder b)
    {
        // Needed for the exclusion constraint that mixes "=" on a uuid with "&&" on a range.
        b.HasPostgresExtension("btree_gist");

        b.Entity<Shop>(e =>
        {
            e.HasIndex(x => x.Slug).IsUnique();
            e.Property(x => x.Slug).HasMaxLength(63);
            e.Property(x => x.Name).HasMaxLength(200);
            e.Property(x => x.TimeZoneId).HasMaxLength(64);
            e.Property(x => x.Language).HasMaxLength(8);
            e.Property(x => x.PaymentAccountId).HasMaxLength(255);
            e.ToTable(t =>
            {
                t.HasCheckConstraint("ck_shops_slug_format", "slug ~ '^[a-z0-9]([a-z0-9-]*[a-z0-9])?$'");
                t.HasCheckConstraint("ck_shops_free_cancellation_minutes", "free_cancellation_minutes >= 0");
            });
        });

        b.Entity<Service>(e =>
        {
            // (shop_id, id) is a key so bookings can reference it with a composite foreign key,
            // which makes it impossible to book one shop's service in another shop.
            e.HasAlternateKey(x => new { x.ShopId, x.Id });
            e.Property(x => x.Name).HasMaxLength(200);
            e.HasOne(x => x.Shop).WithMany(s => s.Services).HasForeignKey(x => x.ShopId).OnDelete(DeleteBehavior.Cascade);
            e.ToTable(t =>
            {
                t.HasCheckConstraint("ck_services_duration", "duration_minutes > 0 AND duration_minutes <= 1440");
                t.HasCheckConstraint("ck_services_amounts", "price_cents >= 0 AND deposit_cents >= 0");
            });
        });

        b.Entity<Staff>(e =>
        {
            e.ToTable("staff");
            e.HasAlternateKey(x => new { x.ShopId, x.Id });
            e.Property(x => x.Name).HasMaxLength(200);
            e.HasOne(x => x.Shop).WithMany(s => s.Staff).HasForeignKey(x => x.ShopId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<OpeningHours>(e =>
        {
            e.ToTable("opening_hours", t => t.HasCheckConstraint("ck_opening_hours_order", "closes > opens"));
            e.HasIndex(x => new { x.ShopId, x.DayOfWeek });
            e.HasOne<Shop>().WithMany().HasForeignKey(x => x.ShopId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<Staff>().WithMany().HasForeignKey(x => new { x.ShopId, x.StaffId })
                .HasPrincipalKey(s => new { s.ShopId, s.Id }).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<TimeOff>(e =>
        {
            e.ToTable("time_off", t => t.HasCheckConstraint("ck_time_off_order", "end_utc > start_utc"));
            e.HasIndex(x => new { x.ShopId, x.StartUtc });
            e.Property(x => x.Note).HasMaxLength(200);
            e.HasOne<Shop>().WithMany().HasForeignKey(x => x.ShopId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<Staff>().WithMany().HasForeignKey(x => new { x.ShopId, x.StaffId })
                .HasPrincipalKey(s => new { s.ShopId, s.Id }).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<Booking>(e =>
        {
            e.Property(x => x.CustomerName).HasMaxLength(200);
            e.Property(x => x.CustomerEmail).HasMaxLength(320);
            e.Property(x => x.CustomerPhone).HasMaxLength(32);
            e.HasIndex(x => new { x.ShopId, x.StartUtc });

            // Restrict, not cascade: deleting a service or staff member must not silently delete bookings.
            e.HasOne<Shop>().WithMany().HasForeignKey(x => x.ShopId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Service).WithMany().HasForeignKey(x => new { x.ShopId, x.ServiceId })
                .HasPrincipalKey(s => new { s.ShopId, s.Id }).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Staff).WithMany().HasForeignKey(x => new { x.ShopId, x.StaffId })
                .HasPrincipalKey(s => new { s.ShopId, s.Id }).OnDelete(DeleteBehavior.Restrict);

            e.ToTable(t =>
            {
                t.HasCheckConstraint("ck_bookings_order", "end_utc > start_utc");
                t.HasCheckConstraint("ck_bookings_hold", "status <> 'pending_payment' OR hold_expires_utc IS NOT NULL");
                t.HasCheckConstraint("ck_bookings_amounts", "deposit_cents >= 0 AND free_cancellation_minutes >= 0");
            });
            // The overlap exclusion constraint (BookingOverlapConstraint) is created in the
            // migration with raw SQL because EF Core cannot model exclusion constraints.
        });

        b.Entity<Payment>(e =>
        {
            e.Property(x => x.Provider).HasMaxLength(32);
            e.Property(x => x.ProviderPaymentId).HasMaxLength(255);
            e.Property(x => x.Currency).HasMaxLength(3).IsFixedLength();
            // One row per provider payment: the anchor for idempotent webhook handling.
            e.HasIndex(x => new { x.Provider, x.ProviderPaymentId }).IsUnique();
            e.HasOne(x => x.Booking).WithMany(bk => bk.Payments).HasForeignKey(x => x.BookingId).OnDelete(DeleteBehavior.Restrict);
            e.ToTable(t => t.HasCheckConstraint("ck_payments_amounts", "amount_cents >= 0 AND platform_fee_cents >= 0 AND platform_fee_cents <= amount_cents"));
        });
    }
}
