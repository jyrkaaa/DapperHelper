using Models;
using Models.Entities;

namespace DAL.Contracts;

public interface IUserRepository
{
    Task<IEnumerable<UserEntity>> GetAllAsync();
    Task<UserEntity?> GetByIdAsync(int id);
    Task<UserEntity?> GetByCredentialAsync(string email, string password);
    Task<CustomUserDto?> GetUserWithTenants(int id);
}