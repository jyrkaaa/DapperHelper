using Models.Entities;
using Models.Enum;

namespace Models;

public class CustomUserDto
{
    public int Id { get; set; }
    public required string Username { get; set; }
    public required string Email { get; set; }
    public IEnumerable<CustomTenantDto> Tenants { get; set; } = new List<CustomTenantDto>();
}

public class CustomTenantDto
{
    public int Id { get; set; }
    public TenantUserStatus Status { get; set; }
    public required string Name { get; set; }
}