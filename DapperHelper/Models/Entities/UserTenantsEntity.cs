using System.ComponentModel.DataAnnotations.Schema;
using Models.Enum;

namespace Models.Entities;

[Table("UserTenants")]
public class UserTenantsEntity
{
    public int UserId { get; set; }
    public int TenantId { get; set; }
    public TenantUserStatus Status { get; set; }
}