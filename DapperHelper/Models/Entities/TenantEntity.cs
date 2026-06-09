using System.ComponentModel.DataAnnotations.Schema;

namespace Models.Entities;

[Table("Tenants")]
public class TenantEntity
{
    public int Id { get; set; }
    public required string Name { get; set; }
}