using DAL.Contracts;
using DAL.Repositories;
using Models;
using Models.Dtos;
using Models.Entities;

namespace BLL.Services;

public class UserService(IUserRepository userRepository) : IUserService
{
    public async Task<IEnumerable<UserDto>> GetAllAsync()
    {
        var users = await userRepository.GetAllAsync();
        var userEntities = users as UserEntity[] ?? users.ToArray();
        if (userEntities.Length == 0) return Array.Empty<UserDto>();
        return userEntities.Select(u => new UserDto
        {
            Id = u.Id,
            Username = u.Username,
            Email = u.Email,
            CreatedAt = u.Created_At,
        });
    }
    
    public async Task<CustomUserDto?> GetUserByIdAsync(int id)
    {
        var entity = await userRepository.GetUserWithTenants(id);
        return entity;
    }

    public async Task<UserDto?> GetUserByCredentialsAsync(string email, string password)
    {
        var entity = await userRepository.GetByCredentialAsync(email, password);
        return entity == null ? null : new UserDto
        {
            Id = entity.Id,
            Username = entity.Username,
            Email = entity.Email,
            CreatedAt = entity.Created_At,
        };
    }
}