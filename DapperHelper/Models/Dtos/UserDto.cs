
namespace Models.Dtos;

public class UserDto
{
    public int Id { get; set; }
    public required string Username { get; set; }
    public required string Email { get; set; }
    
    public required DateTime CreatedAt { get; set; }
}