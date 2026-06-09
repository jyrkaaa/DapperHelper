using DAL.Contracts;
using Microsoft.EntityFrameworkCore;
using Models;
using Models.Entities;

namespace DAL.EF;

public class EfCoreUserRepository(AppDbContext context) : IUserRepository
{
    public async Task<IEnumerable<UserEntity>> GetAllAsync()
    {
        return await context.Users.ToListAsync();
    }

    
    public async Task<UserEntity?> GetByIdAsync(int id)
    {
        return await context.Users.FirstOrDefaultAsync(u => u.Id == id);
    }

    public async Task<UserEntity?> GetByCredentialAsync(string email, string password)
    {
        return await context.Users.FirstOrDefaultAsync(u => u.Email == email && u.Password == password);
    }

    public async Task<CustomUserDto?> GetUserWithTenants(int id)
    {
        // LEFT JOIN mirrors the Dapper QueryBuilder LeftJoin chain:
        //   Users → UserTenants → Tenants
        var rows = await (
            from u in context.Users
            where u.Id == id
            join ut in context.UserTenants on u.Id equals ut.UserId into utGroup
            from ut in utGroup.DefaultIfEmpty()
            join t in context.Tenants on ut!.TenantId equals t.Id into tGroup
            from t in tGroup.DefaultIfEmpty()
            select new { u, ut, t }
        ).ToListAsync();

        if (rows.Count == 0) return null;

        var user = rows[0].u;
        return new CustomUserDto
        {
            Id = user.Id,
            Username = user.Username,
            Email = user.Email,
            Tenants = rows
                .Where(r => r.t != null)
                .Select(r => new CustomTenantDto
                {
                    Id = r.t!.Id,
                    Name = r.t!.Name,
                    Status = r.ut!.Status
                })
                .ToList()
        };
    }
}
