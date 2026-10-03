using Microsoft.EntityFrameworkCore;

namespace Services.Database;

public class DatabaseContext : DbContext
{
    public DbSet<BillingEntitlementDocument> BillingEntitlements => Set<BillingEntitlementDocument>();

    public DbSet<McpKeyDocument> McpKeys => Set<McpKeyDocument>();

    public DbSet<McpUsageDocument> McpUsage => Set<McpUsageDocument>();

    public DbSet<StripeEventDocument> StripeEvents => Set<StripeEventDocument>();

    public DbSet<WorkspaceDocument> Workspaces => Set<WorkspaceDocument>();

    public DbSet<WorkspaceRecordDocument> WorkspaceRecords => Set<WorkspaceRecordDocument>();

    public DatabaseContext()
    {
    }

    public DatabaseContext(DbContextOptions<DatabaseContext> options) : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<BillingEntitlementDocument>(entity =>
        {
            entity.ToContainer("BillingEntitlements");
            entity.HasKey(x => x.Id);
            entity.HasPartitionKey(x => x.UserId);
            entity.Property(x => x.ETag).IsETagConcurrency();
        });

        modelBuilder.Entity<McpKeyDocument>(entity =>
        {
            entity.ToContainer("McpKeys");
            entity.HasKey(x => x.Id);
            entity.HasPartitionKey(x => x.UserId);
            entity.Property(x => x.ETag).IsETagConcurrency();
        });
        
        modelBuilder.Entity<McpUsageDocument>(entity =>
        {
            entity.ToContainer("McpUsage");
            entity.HasKey(x => new { x.Id, x.UserId });
            entity.HasPartitionKey(x => x.UserId);
            // Existing SDK-written usage documents have no discriminator and use these JSON names.
            entity.HasNoDiscriminator();
            entity.Property(x => x.Id).ToJsonProperty("id");
            entity.Property(x => x.UserId).ToJsonProperty("userId");
            entity.Property(x => x.UtcDay).ToJsonProperty("utcDay");
            entity.Property(x => x.DailyCount).ToJsonProperty("dailyCount");
            entity.Property(x => x.RecentAdmissions).ToJsonProperty("recentAdmissions");
            entity.Property(x => x.Ttl).ToJsonProperty("ttl");
            entity.Property(x => x.ETag).IsETagConcurrency();
            entity.HasDefaultTimeToLive(172800);
        });

        modelBuilder.Entity<StripeEventDocument>(entity =>
        {
            entity.ToContainer("StripeEvents");
            entity.HasKey(x => x.Id);
            entity.HasPartitionKey(x => x.PartitionId);
        });

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
    }
}
