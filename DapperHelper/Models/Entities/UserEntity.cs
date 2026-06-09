using System.ComponentModel.DataAnnotations.Schema;

namespace Models.Entities;

[Table("Users")]
public class UserEntity
{
    public int Id { get; set; }
    public required string Username { get; set; }
    public required string Email { get; set; }    
    public required string Password { get; set; }
    
    public DateTime Created_At { get; set; }
}