using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using WarehousePro.Application.Common.Interfaces;
using WarehousePro.Domain.Entities;
using System.Text.Json; // Cần thiết để Serialize JSON

namespace WarehousePro.Infrastructure.Persistence;

public class ApplicationDbContext : DbContext, IApplicationDbContext
{
    // Constructor
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
        : base(options)
    {
    }

    // --- CÁC DB SET (Đảm bảo khớp với project của bạn) ---
    public DbSet<Product> Products => Set<Product>();
    public DbSet<Transaction> Transactions => Set<Transaction>();
    public DbSet<TransactionDetail> TransactionDetails => Set<TransactionDetail>();
    public DbSet<User> Users => Set<User>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<Partner> Partners => Set<Partner>();
    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<Warehouse> Warehouses => Set<Warehouse>();
    public DbSet<Location> Locations => Set<Location>();
    public DbSet<Inventory> Inventories => Set<Inventory>();
    public DbSet<ChatMessage> ChatMessages => Set<ChatMessage>();
    public DbSet<ChatGroup> ChatGroups => Set<ChatGroup>();
    public DbSet<ChatGroupMember> ChatGroupMembers => Set<ChatGroupMember>();
    public DbSet<WarehouseTask> WarehouseTasks => Set<WarehouseTask>();
    public DbSet<EmailTemplate> EmailTemplates => Set<EmailTemplate>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        builder.ApplyConfigurationsFromAssembly(typeof(ApplicationDbContext).Assembly);
        base.OnModelCreating(builder);
    }

    // --- GHI ĐÈ SaveChangesAsync ĐỂ CHÈN LOGIC AUDIT ---
    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        // 1. Trước khi lưu: Chuẩn bị dữ liệu Audit
        var auditEntries = OnBeforeSaveChanges();

        // 2. Lưu dữ liệu chính vào DB
        var result = await base.SaveChangesAsync(cancellationToken);

        // 3. Sau khi lưu: Cập nhật Audit (để lấy ID của các bản ghi mới tạo)
        await OnAfterSaveChanges(auditEntries);

        return result;
    }

    private List<AuditEntry> OnBeforeSaveChanges()
    {
        ChangeTracker.DetectChanges();
        var auditEntries = new List<AuditEntry>();

        foreach (var entry in ChangeTracker.Entries())
        {
            // Bỏ qua nếu là AuditLog, không có thay đổi, hoặc Detached
            if (entry.Entity is AuditLog || entry.State == EntityState.Detached || entry.State == EntityState.Unchanged)
                continue;

            var auditEntry = new AuditEntry(entry);
            auditEntry.TableName = entry.Entity.GetType().Name;
            auditEntry.UserId = "Admin"; // TODO: Thay bằng service lấy UserID thật (VD: _currentUserService.UserId)

            // --- 🔥 LOGIC BỔ SUNG NGỮ CẢNH (CONTEXT) CHO INVENTORY 🔥 ---
            if (entry.Entity is Inventory)
            {
                try
                {
                    // 1. Lấy ProductId một cách an toàn (tránh lỗi biên dịch nếu tên khác)
                    var productIdProp = entry.Properties.FirstOrDefault(p => p.Metadata.Name == "ProductId" || p.Metadata.Name == "ProductID");
                    var productId = productIdProp?.CurrentValue;

                    if (productId != null)
                    {
                        // Truy vấn tên sản phẩm
                        // Lưu ý: Chúng ta dùng Id object vì không biết kiểu dữ liệu Id là int hay Guid, EF sẽ tự lo
                        var productName = this.Products
                            .Where(p => EF.Property<object>(p, "Id") == productId)
                            .Select(p => p.Name)
                            .FirstOrDefault();

                        auditEntry.NewValues["ProductName"] = productName ?? "Unknown Product";
                    }

                    // 2. Lấy WarehouseId an toàn
                    // Hệ thống sẽ thử tìm cột tên "WarehouseId" hoặc "WarehouseID" hoặc "StoreId"
                    var warehouseIdProp = entry.Properties.FirstOrDefault(p =>
                        p.Metadata.Name == "WarehouseId" ||
                        p.Metadata.Name == "WarehouseID" ||
                        p.Metadata.Name == "StoreId");

                    var warehouseId = warehouseIdProp?.CurrentValue;

                    if (warehouseId != null)
                    {
                        // Truy vấn tên kho
                        var warehouseName = this.Warehouses
                            .Where(w => EF.Property<object>(w, "Id") == warehouseId)
                            .Select(w => w.Name)
                            .FirstOrDefault();

                        auditEntry.NewValues["WarehouseName"] = warehouseName ?? "Unknown Warehouse";
                    }
                }
                catch (Exception)
                {
                    // Silent fail: Nếu có lỗi khi lấy thông tin phụ, ta bỏ qua để không chặn quy trình lưu chính
                }
            }
            // -----------------------------------------------------------

            auditEntries.Add(auditEntry);

            foreach (var property in entry.Properties)
            {
                if (property.IsTemporary)
                {
                    auditEntry.TemporaryProperties.Add(property);
                    continue;
                }

                string propertyName = property.Metadata.Name;
                if (property.Metadata.IsPrimaryKey())
                {
                    auditEntry.KeyValues[propertyName] = property.CurrentValue;
                    continue;
                }

                switch (entry.State)
                {
                    case EntityState.Added:
                        auditEntry.AuditType = "CREATE";
                        auditEntry.NewValues[propertyName] = property.CurrentValue;
                        break;

                    case EntityState.Deleted:
                        auditEntry.AuditType = "DELETE";
                        auditEntry.OldValues[propertyName] = property.OriginalValue;
                        break;

                    case EntityState.Modified:
                        if (property.IsModified)
                        {
                            auditEntry.AuditType = "UPDATE";
                            auditEntry.OldValues[propertyName] = property.OriginalValue;
                            auditEntry.NewValues[propertyName] = property.CurrentValue;
                        }
                        break;
                }
            }
        }

        // Lọc bỏ các bản ghi Update rỗng (trừ khi chúng ta đã inject thêm info vào NewValues như Inventory)
        return auditEntries.Where(e => e.HasTemporaryProperties || e.AuditType != "UPDATE" || e.NewValues.Count > 0).ToList();
    }

    private async Task OnAfterSaveChanges(List<AuditEntry> auditEntries)
    {
        if (auditEntries == null || auditEntries.Count == 0) return;

        foreach (var auditEntry in auditEntries)
        {
            foreach (var prop in auditEntry.TemporaryProperties)
            {
                if (prop.Metadata.IsPrimaryKey())
                    auditEntry.KeyValues[prop.Metadata.Name] = prop.CurrentValue;
                else
                    auditEntry.NewValues[prop.Metadata.Name] = prop.CurrentValue;
            }
            AuditLogs.Add(auditEntry.ToAuditLog());
        }
        await base.SaveChangesAsync();
    }
}

// --- HELPER CLASS ---
public class AuditEntry
{
    public AuditEntry(EntityEntry entry)
    {
        Entry = entry;
    }
    public EntityEntry Entry { get; }
    public string UserId { get; set; }
    public string TableName { get; set; }
    public Dictionary<string, object> KeyValues { get; } = new();
    public Dictionary<string, object> OldValues { get; } = new();
    public Dictionary<string, object> NewValues { get; } = new();
    public List<PropertyEntry> TemporaryProperties { get; } = new();
    public string AuditType { get; set; }
    public bool HasTemporaryProperties => TemporaryProperties.Any();

    public AuditLog ToAuditLog()
    {
        var audit = new AuditLog();
        audit.UserId = UserId;
        audit.Action = AuditType;
        audit.TableName = TableName;
        audit.CreatedDate = DateTime.UtcNow;
        audit.RecordId = JsonSerializer.Serialize(KeyValues);
        audit.OldValues = OldValues.Count == 0 ? null : JsonSerializer.Serialize(OldValues);
        audit.NewValues = NewValues.Count == 0 ? null : JsonSerializer.Serialize(NewValues);
        return audit;
    }
}