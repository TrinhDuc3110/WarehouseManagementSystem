using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using WarehousePro.Domain.Common;

namespace WarehousePro.Domain.Entities;

public class Partner : BaseEntity
{
    public string Name { get; set; } = string.Empty; 
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string? Address { get; set; }
    public string Type { get; set; } = "SUPPLIER"; // "SUPPLIER" (NCC) hoặc "CUSTOMER" (Khách)
    public decimal DebtAmount { get; set; } = 0;
    public bool IsActive { get; set; } = true;
}