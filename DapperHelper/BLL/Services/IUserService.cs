using Models;
using Models.Dtos;

namespace BLL.Services;

public interface IUserService
{
    Task<IEnumerable<UserDto>> GetAllAsync();
    Task<CustomUserDto?> GetUserByIdAsync(int id);
    Task<UserDto?> GetUserByCredentialsAsync(string email, string password);
}