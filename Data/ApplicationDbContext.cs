using Microsoft.EntityFrameworkCore;
using RestaurantReservation.Models;
using System.Linq;

namespace RestaurantReservation.Data
{
    public class ApplicationDbContext : DbContext
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
            : base(options)
        {
        }

        // DbSets
        public DbSet<Admin> Admins => Set<Admin>();
        public DbSet<Customer> Customers => Set<Customer>();
        public DbSet<Table> Tables => Set<Table>();
        public DbSet<Reservation> Reservations => Set<Reservation>();
        public DbSet<Session> Sessions => Set<Session>();
        public DbSet<Order> Orders => Set<Order>();
        public DbSet<OrderItem> OrderItems => Set<OrderItem>();
        public DbSet<MenuItem> MenuItems => Set<MenuItem>();
        public DbSet<Transaction> Transactions => Set<Transaction>();
        public DbSet<Feedback> Feedbacks => Set<Feedback>();
        public DbSet<RestaurantSettings> RestaurantSettings => Set<RestaurantSettings>();
        public DbSet<BusinessHours> BusinessHours => Set<BusinessHours>();
        public DbSet<Announcement> Announcements => Set<Announcement>();
        public DbSet<Promotion> Promotions => Set<Promotion>();

        public override int SaveChanges(bool acceptAllChangesOnSuccess)
        {
            AssignMissingStringIds();
            return base.SaveChanges(acceptAllChangesOnSuccess);
        }

        public override Task<int> SaveChangesAsync(
            bool acceptAllChangesOnSuccess,
            CancellationToken cancellationToken = default)
        {
            AssignMissingStringIds();
            return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
        }

        private void AssignMissingStringIds()
        {
            foreach (var entry in ChangeTracker.Entries().Where(entry => entry.State == EntityState.Added))
            {
                var primaryKey = entry.Metadata.FindPrimaryKey();
                if (primaryKey is not { Properties.Count: 1 } || primaryKey.Properties[0].ClrType != typeof(string))
                {
                    continue;
                }

                var idProperty = entry.Property(primaryKey.Properties[0].Name);
                if (string.IsNullOrWhiteSpace(idProperty.CurrentValue as string))
                {
                    idProperty.CurrentValue = Guid.NewGuid().ToString("N");
                }
            }
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            ConfigureStringPrimaryKeys(modelBuilder);

            // -------------------------------------------------------------
            // 1. ADMIN
            // -------------------------------------------------------------
            modelBuilder.Entity<Admin>(entity =>
            {
                entity.HasKey(a => a.Id);
                entity.Property(a => a.Id).ValueGeneratedNever();
                entity.HasIndex(a => a.Email).IsUnique();

                entity.Property(a => a.Role)
                      .HasConversion<string>()
                      .HasMaxLength(20);
            });

            // -------------------------------------------------------------
            // 2. CUSTOMER
            // -------------------------------------------------------------
            modelBuilder.Entity<Customer>(entity =>
            {
                entity.HasKey(c => c.Id);
                entity.Property(c => c.Id).ValueGeneratedNever();
            });

            // -------------------------------------------------------------
            // 3. TABLE
            // -------------------------------------------------------------
            modelBuilder.Entity<Table>(entity =>
            {
                entity.HasKey(t => t.Id);
                entity.Property(t => t.Id).ValueGeneratedNever();
            });

            // -------------------------------------------------------------
            // 4. RESERVATION
            // -------------------------------------------------------------
            modelBuilder.Entity<Reservation>(entity =>
            {
                entity.HasKey(r => r.Id);
                entity.Property(r => r.Id).ValueGeneratedNever();

                entity.Property(r => r.Status)
                      .HasConversion<string>()
                      .HasMaxLength(20);

                entity.HasOne(r => r.Table)
                      .WithMany()
                      .HasForeignKey(r => r.TableId)
                      .OnDelete(DeleteBehavior.Restrict);

                entity.HasOne(r => r.Customer)
                      .WithMany()
                      .HasForeignKey(r => r.CustomerId)
                      .OnDelete(DeleteBehavior.Restrict);
            });

            // -------------------------------------------------------------
            // 5. SESSION
            // -------------------------------------------------------------
            modelBuilder.Entity<Session>(entity =>
            {
                entity.HasKey(s => s.Id);
                entity.Property(s => s.Id).ValueGeneratedNever();
            });

            // -------------------------------------------------------------
            // 6. ORDER & ORDERITEM
            // -------------------------------------------------------------
            modelBuilder.Entity<Order>(entity =>
            {
                entity.HasKey(o => o.Id);
                entity.Property(o => o.Id).ValueGeneratedNever();

                entity.Property(o => o.Status)
                      .HasConversion<string>()
                      .HasMaxLength(20);

                // 1-to-1 Relationship: Session <-> Order
                entity.HasOne(o => o.Session)
                      .WithOne(s => s.Order)
                      .HasForeignKey<Order>(o => o.SessionId)
                      .OnDelete(DeleteBehavior.Cascade);

                entity.HasOne(o => o.Customer)
                      .WithMany()
                      .HasForeignKey(o => o.CustomerId)
                      .OnDelete(DeleteBehavior.Restrict);
            });

            modelBuilder.Entity<OrderItem>(entity =>
            {
                entity.HasKey(oi => oi.Id);
                entity.Property(oi => oi.Id).ValueGeneratedNever();

                entity.HasOne(oi => oi.Order)
                      .WithMany()
                      .HasForeignKey(oi => oi.OrderId)
                      .OnDelete(DeleteBehavior.Cascade);

                entity.HasOne(oi => oi.MenuItem)
                      .WithMany()
                      .HasForeignKey(oi => oi.MenuItemId)
                      .OnDelete(DeleteBehavior.Restrict);
            });

            // -------------------------------------------------------------
            // 7. MENUITEM
            // -------------------------------------------------------------
            modelBuilder.Entity<MenuItem>(entity =>
            {
                entity.HasKey(m => m.Id);
                entity.Property(m => m.Id).ValueGeneratedNever();

                entity.Property(m => m.MenuItemType)
                      .HasConversion<string>()
                      .HasMaxLength(30);
            });

            // -------------------------------------------------------------
            // 8. TRANSACTION (Stripe Sandbox Integration)
            // -------------------------------------------------------------
            modelBuilder.Entity<Transaction>(entity =>
            {
                entity.HasKey(t => t.Id);
                entity.Property(t => t.Id).ValueGeneratedNever();

                entity.Property(t => t.Currency).HasConversion<string>().HasMaxLength(10);
                entity.Property(t => t.Status).HasConversion<string>().HasMaxLength(20);
                entity.Property(t => t.PaymentMethod).HasConversion<string>().HasMaxLength(20);

                entity.HasOne(t => t.Order)
                      .WithMany()
                      .HasForeignKey(t => t.OrderId)
                      .OnDelete(DeleteBehavior.Restrict);
            });

            // -------------------------------------------------------------
            // 9. FEEDBACK
            // -------------------------------------------------------------
            modelBuilder.Entity<Feedback>(entity =>
            {
                entity.HasKey(f => f.Id);
                entity.Property(f => f.Id).ValueGeneratedNever();

                entity.HasOne(f => f.MenuItem)
                      .WithMany()
                      .HasForeignKey(f => f.MenuItemId)
                      .OnDelete(DeleteBehavior.Cascade);

                entity.HasOne(f => f.Customer)
                      .WithMany()
                      .HasForeignKey(f => f.CustomerId)
                      .OnDelete(DeleteBehavior.Restrict);
            });

            // -------------------------------------------------------------
            // 10. RESTAURANT SETTINGS & BUSINESS HOURS
            // -------------------------------------------------------------
            modelBuilder.Entity<RestaurantSettings>(entity =>
            {
                entity.HasKey(s => s.Id);
                entity.Property(s => s.Id).ValueGeneratedNever();

                entity.Property(s => s.DefaultCurrency)
                      .HasConversion<string>()
                      .HasMaxLength(10);
            });

            modelBuilder.Entity<BusinessHours>(entity =>
            {
                entity.HasKey(b => b.Id);
                entity.Property(b => b.Id).ValueGeneratedNever();

                entity.Property(b => b.DayOfWeek)
                      .HasConversion<string>()
                      .HasMaxLength(15);

                entity.HasOne(b => b.RestaurantSettings)
                      .WithMany(s => s.BusinessHours)
                      .HasForeignKey(b => b.RestaurantSettingsId)
                      .OnDelete(DeleteBehavior.Cascade);
            });

            // -------------------------------------------------------------
            // 11. ANNOUNCEMENT
            // -------------------------------------------------------------
            modelBuilder.Entity<Announcement>(entity =>
            {
                entity.HasKey(a => a.Id);
                entity.Property(a => a.Id).ValueGeneratedNever();
            });

            // -------------------------------------------------------------
            // 12. PROMOTION
            // -------------------------------------------------------------
            modelBuilder.Entity<Promotion>(entity =>
            {
                entity.HasKey(p => p.Id);
                entity.Property(p => p.Id).ValueGeneratedNever();

                // If Promotion has string enums or specific properties (e.g. DiscountType), configure them here:
                // entity.Property(p => p.DiscountType)
                //       .HasConversion<string>()
                //       .HasMaxLength(20);
            });
        }

        private static void ConfigureStringPrimaryKeys(ModelBuilder modelBuilder)
        {
            foreach (var entityType in modelBuilder.Model.GetEntityTypes())
            {
                var primaryKey = entityType.FindPrimaryKey();
                if (primaryKey is not { Properties.Count: 1 } || primaryKey.Properties[0].ClrType != typeof(string))
                {
                    continue;
                }

                modelBuilder.Entity(entityType.ClrType)
                    .Property(primaryKey.Properties[0].Name)
                    .ValueGeneratedNever();
            }
        }
    }
}