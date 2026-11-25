using Microsoft.EntityFrameworkCore;

namespace Services.Database;

public class DatabaseContext : DbContext
{
    public DbSet<BillingEntitlementDocument> BillingEntitlements => Set<BillingEntitlementDocument>();

    public DbSet<WorkspaceConversationDocument> Conversations => Set<WorkspaceConversationDocument>();

    public DbSet<McpKeyDocument> McpKeys => Set<McpKeyDocument>();

    public DbSet<McpUsageDocument> McpUsage => Set<McpUsageDocument>();

    public DbSet<WorkspaceProjectDocument> Projects => Set<WorkspaceProjectDocument>();

    public DbSet<ProjectIndexDocument> ProjectIndexes => Set<ProjectIndexDocument>();

    public DbSet<WorkspaceSessionDocument> Sessions => Set<WorkspaceSessionDocument>();

    public DbSet<StripeEventDocument> StripeEvents => Set<StripeEventDocument>();

    public DbSet<WorkspaceTaskDocument> Tasks => Set<WorkspaceTaskDocument>();

    public DbSet<UserDocument> Users => Set<UserDocument>();

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
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<BillingEntitlementDocument>(entity =>
        {
            entity.ToContainer("BillingEntitlements");
            entity.Property(x => x.SubscriptionInterval).HasConversion<string>();
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
            entity.OwnsMany(x => x.ApiAdmissions, admission =>
            {
                admission.ToJsonProperty("apiAdmissions");
                admission.Property(x => x.Scope).HasConversion<string>().ToJsonProperty("scope");
                admission.Property(x => x.Timestamp).ToJsonProperty("timestamp");
            });
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

        modelBuilder.Entity<UserDocument>(entity =>
        {
            entity.ToContainer("Users");
            entity.HasKey(x => x.Id);
            entity.HasPartitionKey(x => x.UserId);
            entity.Property(x => x.ETag).IsETagConcurrency();
        });

        modelBuilder.Entity<WorkspaceDocument>(entity =>
        {
            entity.ToContainer("WorkspaceDocuments");
            entity.HasKey(x => x.Id);
            entity.HasPartitionKey(x => x.UserId);
            entity.Property(x => x.ETag).IsETagConcurrency();
            entity.OwnsOne(x => x.PendingProjectArchive);
            entity.OwnsOne(x => x.Workspace, workspace =>
            {
                workspace.Property(x => x.Plan).HasConversion<string>();
                workspace.Property(x => x.FirstRunState).HasConversion<string>();
                workspace.OwnsMany(x => x.Projects);
                workspace.OwnsMany(x => x.Tasks, task => task.Property(x => x.Priority).HasConversion<string>());
                workspace.OwnsMany(x => x.Sessions, session => session.OwnsMany(x => x.Intervals));
                workspace.OwnsMany(x => x.Conversations, conversation => conversation.OwnsMany(x => x.Messages, message => message.Property(x => x.Role).HasConversion<string>()));
                workspace.OwnsOne(x => x.Timer, timer =>
                {
                    timer.Property(x => x.Phase).HasConversion<string>();
                    timer.OwnsMany(x => x.CompletedIntervals);
                });
            });
        });

        modelBuilder.Entity<WorkspaceRecordDocument>(entity =>
        {
            entity.ToContainer("WorkspaceRecords");
            entity.HasKey(x => x.Id);
            entity.HasPartitionKey(x => x.UserId);
            entity.OwnsOne(x => x.Project);
            entity.OwnsOne(x => x.Task, task => task.Property(x => x.Priority).HasConversion<string>());
            entity.OwnsOne(x => x.Focus, focus => focus.OwnsMany(x => x.Intervals));
            entity.OwnsOne(x => x.Conversation, conversation => conversation.OwnsMany(x => x.Messages, message => message.Property(x => x.Role).HasConversion<string>()));
            entity.OwnsOne(x => x.Timer, timer =>
            {
                timer.Property(x => x.Phase).HasConversion<string>();
                timer.OwnsMany(x => x.CompletedIntervals);
            });
        });

        ConfigureWorkspaceItem<WorkspaceProjectDocument>(modelBuilder, "Projects");
        ConfigureWorkspaceItem<WorkspaceTaskDocument>(modelBuilder, "Tasks");
        ConfigureWorkspaceItem<WorkspaceSessionDocument>(modelBuilder, "Sessions");
        ConfigureWorkspaceItem<WorkspaceConversationDocument>(modelBuilder, "Conversations");
        modelBuilder.Entity<ProjectIndexDocument>(entity =>
        {
            entity.ToContainer("Projects");
            entity.HasKey(x => x.Id);
            entity.HasPartitionKey(x => x.UserId);
            entity.Property(x => x.ETag).IsETagConcurrency();
        });
        modelBuilder.Entity<WorkspaceProjectDocument>().OwnsOne(x => x.Project);
        modelBuilder.Entity<WorkspaceTaskDocument>().OwnsOne(x => x.Task, task => task.Property(x => x.Priority).HasConversion<string>());
        modelBuilder.Entity<WorkspaceSessionDocument>().OwnsOne(x => x.Session, session => session.OwnsMany(x => x.Intervals));
        modelBuilder.Entity<WorkspaceConversationDocument>().OwnsOne(x => x.Conversation, conversation => conversation.OwnsMany(x => x.Messages, message => message.Property(x => x.Role).HasConversion<string>()));
    }

    private static void ConfigureWorkspaceItem<T>(ModelBuilder modelBuilder, string container) where T : class, IWorkspaceItemDocument
    {
        modelBuilder.Entity<T>(entity =>
        {
            entity.ToContainer(container);
            entity.HasKey(x => x.Id);
            entity.HasPartitionKey(x => x.UserId);
            entity.Property(x => x.ETag).IsETagConcurrency();
        });
    }
}
