using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using WarehousePro.Domain.Entities;

namespace WarehousePro.Application.Common.Interfaces
{
    public interface IApplicationDbContext
    {
        DbSet<Product> Products { get; }
        DbSet<Transaction> Transactions { get; }
        DbSet<TransactionDetail> TransactionDetails { get; }
        DbSet<User> Users { get; }
        DbSet<AuditLog> AuditLogs { get; }
        DbSet<Partner> Partners { get; }
        DbSet<Payment> Payments { get; }
        DbSet<Warehouse> Warehouses { get; }
        DbSet<Location> Locations { get; } 
        DbSet<Inventory> Inventories { get; }
        DbSet<ChatMessage> ChatMessages { get; }
        DbSet<ChatGroupMember> ChatGroupMembers { get; }
        DbSet<ChatGroup> ChatGroups { get; }
        DbSet<WarehouseTask> WarehouseTasks { get; }
        DbSet<EmailTemplate> EmailTemplates { get; }

        DatabaseFacade Database { get; }

        Task<int> SaveChangesAsync(CancellationToken cancellationToken);
    }
}
