using DAL.Contracts;
using Dapper;
using Models;
using Models.Entities;

namespace DAL.RawDapper.Repositories;

public class UserRepository(IDbConnectionFactory connectionFactory) : IUserRepository
{
    public async Task<IEnumerable<UserEntity>> GetAllAsync()
    {
        using var conn = await connectionFactory.CreateConnectionAsync();
        return await conn.QueryAsync<UserEntity>(
            "SELECT Id, Username, Email, Password, Created_At FROM Users");
    }

    public async Task<UserEntity?> GetByIdAsync(int id)
    {
        using var conn = await connectionFactory.CreateConnectionAsync();
        return await conn.QuerySingleOrDefaultAsync<UserEntity>(
            "SELECT Id, Username, Email, Password, Created_At FROM Users WHERE Id = @Id",
            new { Id = id });
    }

    public async Task<UserEntity?> GetByCredentialAsync(string email, string password)
    {
        using var conn = await connectionFactory.CreateConnectionAsync();
        return await conn.QuerySingleOrDefaultAsync<UserEntity>(
            "SELECT Id, Username, Email, Password, Created_At FROM Users WHERE Email = @Email AND Password = @Password",
            new { Email = email, Password = password });
    }

    public async Task<CustomUserDto?> GetUserWithTenants(int id)
    {
        using var conn = await connectionFactory.CreateConnectionAsync();
        const string sql =
            @"SELECT u.Id, u.Username, u.Email,
                     t.Id, t.Name, ut.Status
              FROM Users u
              LEFT JOIN UserTenants ut ON u.Id = ut.UserId
              LEFT JOIN Tenants t ON ut.TenantId = t.Id
              WHERE u.Id = @Id";

        var userDict = new Dictionary<int, CustomUserDto>();

        await conn.QueryAsync<CustomUserDto, CustomTenantDto, CustomUserDto>(
            sql,
            (user, tenant) =>
            {
                if (!userDict.TryGetValue(user.Id, out var existing))
                {
                    existing = user;
                    userDict[user.Id] = existing;
                }
                if (tenant is { Id: not 0 })
                    ((List<CustomTenantDto>)existing.Tenants).Add(tenant);
                return existing;
            },
            new { Id = id },
            splitOn: "Id");

        return userDict.Values.FirstOrDefault();
    }
}
