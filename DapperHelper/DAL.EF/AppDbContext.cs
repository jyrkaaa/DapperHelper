using Microsoft.EntityFrameworkCore;
using Models.Entities;

namespace DAL.EF;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<UserEntity> Users => Set<UserEntity>();
    public DbSet<TenantEntity> Tenants => Set<TenantEntity>();
    public DbSet<UserTenantsEntity> UserTenants => Set<UserTenantsEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<UserTenantsEntity>().HasKey(e => new { e.UserId, e.TenantId });
    }
}
