using Microsoft.EntityFrameworkCore;
using TenderAI.Api.Data.Entities;

namespace TenderAI.Api.Data
{
    public class TenderAiDbContext(DbContextOptions<TenderAiDbContext> options) : DbContext(options)
    {
        public DbSet<CustomerEntity> Customers => Set<CustomerEntity>();
        public DbSet<CustomerRiskFeaturesEntity> CustomerRiskFeatures => Set<CustomerRiskFeaturesEntity>();
        public DbSet<CustomerPaymentEntity> CustomerPayments => Set<CustomerPaymentEntity>();
        public DbSet<CustomerAccountEntity> CustomerAccounts => Set<CustomerAccountEntity>();
        public DbSet<NotificationEntity> Notifications => Set<NotificationEntity>();
        public DbSet<TenderEntity> Tenders => Set<TenderEntity>();
        public DbSet<TenderProductEntity> TenderProducts => Set<TenderProductEntity>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<CustomerEntity>(b =>
            {
                b.ToTable("Customers");
                b.HasKey(c => c.CustomerId);
                b.Property(c => c.CustomerId).HasMaxLength(20);
                b.Property(c => c.Name).HasMaxLength(200);
                b.Property(c => c.TaxCode).HasMaxLength(9);
                b.Property(c => c.Type).HasMaxLength(50);
                b.Property(c => c.IndustryCode).HasMaxLength(10);
                b.Property(c => c.IndustryName).HasMaxLength(200);
                b.Property(c => c.RegionName).HasMaxLength(100);
                b.Property(c => c.DistrictName).HasMaxLength(100);
                b.Property(c => c.LanguageCode).HasMaxLength(2);

                // Deleting a customer deletes its risk features, payments and notifications
                b.HasOne(c => c.RiskFeatures)
                    .WithOne(r => r.Customer)
                    .HasForeignKey<CustomerRiskFeaturesEntity>(r => r.CustomerId)
                    .OnDelete(DeleteBehavior.Cascade);
                b.HasMany(c => c.Payments)
                    .WithOne(p => p.Customer)
                    .HasForeignKey(p => p.CustomerId)
                    .OnDelete(DeleteBehavior.Cascade);
                b.HasMany(c => c.Accounts)
                    .WithOne(a => a.Customer)
                    .HasForeignKey(a => a.CustomerId)
                    .OnDelete(DeleteBehavior.Cascade);
                b.HasMany(c => c.Notifications)
                    .WithOne(n => n.Customer)
                    .HasForeignKey(n => n.CustomerId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<CustomerRiskFeaturesEntity>(b =>
            {
                b.ToTable("CustomerRiskFeatures");
                b.HasKey(r => r.CustomerId);
            });

            modelBuilder.Entity<CustomerPaymentEntity>(b =>
            {
                b.ToTable("CustomerPayments");
                b.Property(p => p.PurposeDescription).HasMaxLength(500);
            });

            modelBuilder.Entity<CustomerAccountEntity>(b =>
            {
                b.ToTable("CustomerAccounts");
                b.HasIndex(a => new { a.CustomerId, a.Currency }).IsUnique();
            });

            modelBuilder.Entity<NotificationEntity>(b =>
            {
                b.ToTable("Notifications");
                b.HasKey(n => n.Id);
                b.Property(n => n.Id).HasMaxLength(20);
                b.Property(n => n.LotId).HasMaxLength(20);
                b.Property(n => n.CurrencyCode).HasMaxLength(3);
                b.Property(n => n.SourceUrl).HasMaxLength(1000);
                b.HasIndex(n => new { n.CustomerId, n.CreatedAt });
                b.OwnsOne(n => n.Offer);
            });

            modelBuilder.Entity<TenderEntity>(b =>
            {
                b.ToTable("Tenders");
                b.HasKey(t => t.Id);
                // The ID comes from UZEX, not from the database
                b.Property(t => t.Id).ValueGeneratedNever();
                b.Property(t => t.DisplayNo).HasMaxLength(30);
                b.Property(t => t.Source).HasMaxLength(20);
                b.Property(t => t.BuyerTin).HasMaxLength(20);
                b.Property(t => t.CurrencyCode).HasMaxLength(3);
                b.Property(t => t.SummaryError).HasMaxLength(2000);
                b.HasIndex(t => t.EndDate);

                // Deleting a tender deletes its products
                b.HasMany(t => t.Products)
                    .WithOne(p => p.Tender)
                    .HasForeignKey(p => p.TenderId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<TenderProductEntity>(b =>
            {
                b.ToTable("TenderProducts");
                b.Property(p => p.ProductCode).HasMaxLength(30);
                b.Property(p => p.TechnicalSpecifications);
                b.HasIndex(p => p.ProductCode);
            });
        }
    }
}
