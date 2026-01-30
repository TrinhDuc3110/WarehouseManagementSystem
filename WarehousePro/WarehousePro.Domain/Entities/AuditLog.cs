using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using WarehousePro.Domain.Common;
namespace WarehousePro.Domain.Entities;

public class AuditLog : BaseEntity
{
    public string UserId { get; set; } = string.Empty;    
    public string Action { get; set; } = string.Empty;    
    public string TableName { get; set; } = string.Empty; 
    public string RecordId { get; set; } = string.Empty; 
    public string? OldValues { get; set; }       
    public string? NewValues { get; set; }

    public bool IsSuspicious { get; set; } = false; 
    public string? RiskNote { get; set; }
}