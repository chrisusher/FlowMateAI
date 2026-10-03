using Microsoft.EntityFrameworkCore;

namespace Services.Database;

public class DatabaseContext : DbContext
{
    public DbSet<WorkspaceDocument> Workspaces => Set<WorkspaceDocument>();
    public DbSet<WorkspaceRecordDocument> WorkspaceRecords => Set<WorkspaceRecordDocument>();
    public DbSet<BillingEntitlementDocument> BillingEntitlements => Set<BillingEntitlementDocument>();
    public DbSet<StripeEventDocument> StripeEvents => Set<StripeEventDocument>();
    public DbSet<McpKeyDocument> McpKeys => Set<McpKeyDocument>();
    public DatabaseContext()
    {
    }

    public DatabaseContext(DbContextOptions<DatabaseContext> options) : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.Entity<WorkspaceDocument>(entity =>
        {
            entity.ToContainer("WorkspaceDocuments");
            entity.HasKey(x => x.Id);
            entity.HasPartitionKey(x => x.UserId);
            entity.Property(x => x.ETag).IsETagConcurrency();
        });
        modelBuilder.Entity<WorkspaceRecordDocument>(entity =>
        {
            entity.ToContainer("WorkspaceRecords");
            entity.HasKey(x => x.Id);
            entity.HasPartitionKey(x => x.UserId);
        });
        modelBuilder.Entity<BillingEntitlementDocument>(entity =>
        {
            entity.ToContainer("BillingEntitlements");
            entity.HasKey(x => x.Id);
            entity.HasPartitionKey(x => x.UserId);
            entity.Property(x => x.ETag).IsETagConcurrency();
        });
        modelBuilder.Entity<StripeEventDocument>(entity =>
        {
            entity.ToContainer("StripeEvents");
            entity.HasKey(x => x.Id);
            entity.HasPartitionKey(x => x.PartitionId);
        });
        modelBuilder.Entity<McpKeyDocument>(entity =>
        {
            entity.ToContainer("McpKeys");
            entity.HasKey(x => x.Id);
            entity.HasPartitionKey(x => x.UserId);
            entity.Property(x => x.ETag).IsETagConcurrency();
        });
    }
}
