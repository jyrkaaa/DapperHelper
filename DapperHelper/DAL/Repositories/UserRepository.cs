using DAL.Contracts;
using Dapper;
using Models;
using Models.Entities;
using QueryLib;

namespace DAL.Repositories;

public class UserRepository(IDbConnectionFactory connectionFactory) : IUserRepository
{
    public async Task<IEnumerable<UserEntity>> GetAllAsync()
    {
        using var conn = await connectionFactory.CreateConnectionAsync();
        var query = QueryBuilder<UserEntity>.From()
            .Build();
        return await conn.QueryAsync<UserEntity>(query.Sql, query.Parameters);
    }

    public async Task<UserEntity?> GetByIdAsync(int id)
    {
        using var conn = await connectionFactory.CreateConnectionAsync();
        var query = QueryBuilder<UserEntity>.From()
            .Where(u => u.Id == id)
            .Build();
        return await conn.QuerySingleOrDefaultAsync<UserEntity>(query.Sql, query.Parameters);
    }

    public async Task<UserEntity?> GetByCredentialAsync(string email, string password)
    {
        using var conn = await connectionFactory.CreateConnectionAsync();
        var query = QueryBuilder<UserEntity>.From()
            .Where(u => u.Email == email && u.Password == password)
            .Build();
        return await conn.QuerySingleOrDefaultAsync<UserEntity>(query.Sql, query.Parameters);
    }

    public async Task<CustomUserDto?> GetUserWithTenants(int id)
    {
        using var conn = await connectionFactory.CreateConnectionAsync();

        // Users → UserTenants (junction, carries Status) → Tenants
        var query = QueryBuilder<UserEntity>.From()
            .LeftJoin<UserTenantsEntity>((u, ut) => u.Id == ut.UserId)
            .LeftJoin<UserTenantsEntity, TenantEntity>((ut, t) => ut.TenantId == t.Id)
            .Where(u => u.Id == id)
            .SelectFrom<UserEntity>(u => u.Id, u => u.Username, u => u.Email)
            .SelectFrom<UserTenantsEntity>(ut => ut.UserId, ut => ut.Status)  // UserId is the splitOn anchor
            .SelectFrom<TenantEntity>(t => t.Id, t => t.Name)
            .Build();

        // Columns: t0.Id, t0.Username, t0.Email | t1.UserId, t1.Status | t2.Id, t2.Name
        // splitOn:                              ^ "UserId"              ^ "Id"
        CustomUserDto? result = null;

        await conn.QueryAsync<UserEntity, UserTenantsEntity, TenantEntity, CustomUserDto>(
            query.Sql,
            (user, junction, tenant) =>
            {
                result ??= new CustomUserDto
                {
                    Id = user.Id,
                    Username = user.Username,
                    Email = user.Email
                };

                if (tenant is not null)
                    result.Tenants = result.Tenants.Append(new CustomTenantDto
                    {
                        Id = tenant.Id,
                        Name = tenant.Name,
                        Status = junction.Status
                    });

                return result;
            },
            query.Parameters,
            splitOn: "UserId,Id");

        return result;
    }
}
