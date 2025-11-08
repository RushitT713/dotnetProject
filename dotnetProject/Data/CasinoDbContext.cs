using dotnetProject.Models;
using Microsoft.EntityFrameworkCore;
using System.Collections.Generic;
using System.Reflection.Emit;

namespace dotnetProject.Data
{
    public class CasinoDbContext : DbContext
    {
        public CasinoDbContext(DbContextOptions<CasinoDbContext> options)
            : base(options)
        {
        }

        public DbSet<Player> Players { get; set; }
        public DbSet<Transaction> Transactions { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // Configure Player
            modelBuilder.Entity<Player>(entity =>
            {
                entity.HasIndex(e => e.PlayerId).IsUnique();
                entity.Property(e => e.Balance).HasPrecision(18, 2);
            });

            // Configure Transaction
            modelBuilder.Entity<Transaction>(entity =>
            {
                entity.HasOne(d => d.Player)
                    .WithMany(p => p.Transactions)
                    .HasForeignKey(d => d.PlayerId)
                    .OnDelete(DeleteBehavior.Cascade);

                entity.Property(e => e.AmountBefore).HasPrecision(18, 2);
                entity.Property(e => e.AmountChange).HasPrecision(18, 2);
                entity.Property(e => e.AmountAfter).HasPrecision(18, 2);
            });
        }
    }
}