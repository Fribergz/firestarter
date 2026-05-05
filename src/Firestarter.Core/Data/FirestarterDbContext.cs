using Firestarter.Core.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Firestarter.Core.Data;

public class FirestarterDbContext(DbContextOptions<FirestarterDbContext> options) : DbContext(options)
{
    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.Properties<DateTimeOffset>()
            .HaveConversion<DateTimeOffsetToBinaryConverter>();
        configurationBuilder.Properties<DateTimeOffset?>()
            .HaveConversion<DateTimeOffsetToBinaryConverter>();
    }

    public DbSet<GitlabSettings> GitlabSettings => Set<GitlabSettings>();
    public DbSet<JenkinsSettings> JenkinsSettings => Set<JenkinsSettings>();
    public DbSet<Project> Projects => Set<Project>();
    public DbSet<Branch> Branches => Set<Branch>();
    public DbSet<MergeRequest> MergeRequests => Set<MergeRequest>();
    public DbSet<Extension> Extensions => Set<Extension>();
    public DbSet<ExtensionRun> ExtensionRuns => Set<ExtensionRun>();
    public DbSet<SyncCursor> SyncCursors => Set<SyncCursor>();
    public DbSet<IdeRegistration> IdeRegistrations => Set<IdeRegistration>();
    public DbSet<KeyValueSetting> KeyValueSettings => Set<KeyValueSetting>();
    public DbSet<ApiCallLog> ApiCallLogs => Set<ApiCallLog>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<Project>(e =>
        {
            e.HasIndex(x => x.GitlabId).IsUnique();
            e.HasIndex(x => x.PathWithNamespace);
            e.Property(x => x.Name).HasMaxLength(512);
            e.Property(x => x.PathWithNamespace).HasMaxLength(1024);
            e.Property(x => x.JenkinsJobPath).HasMaxLength(512);
            e.Property(x => x.JenkinsPipelinesCacheJson).HasColumnType("TEXT");
            e.Property(x => x.JenkinsPipelinesCacheComplete).HasColumnType("INTEGER");
        });

        b.Entity<Branch>(e =>
        {
            e.HasIndex(x => new { x.ProjectId, x.Name }).IsUnique();
            e.Property(x => x.Name).HasMaxLength(512);
            e.Property(x => x.Sha).HasMaxLength(64);
            e.HasOne(x => x.Project).WithMany(p => p.Branches)
                .HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<MergeRequest>(e =>
        {
            e.HasIndex(x => new { x.ProjectId, x.GitlabIid }).IsUnique();
            e.HasIndex(x => x.State);
            e.Property(x => x.State).HasMaxLength(32);
            e.HasOne(x => x.Project).WithMany(p => p.MergeRequests)
                .HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<Extension>(e =>
        {
            e.HasIndex(x => x.Name).IsUnique();
            e.Property(x => x.Name).HasMaxLength(256);
            e.Property(x => x.SettingsValuesJson).HasColumnType("TEXT");
        });

        b.Entity<ExtensionRun>(e =>
        {
            e.HasIndex(x => x.ExtensionId);
            e.HasIndex(x => x.ProjectId);
            e.HasIndex(x => x.StartedAt);
            e.HasOne(x => x.Extension).WithMany()
                .HasForeignKey(x => x.ExtensionId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Project).WithMany()
                .HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<SyncCursor>(e =>
        {
            e.HasIndex(x => new { x.Entity, x.Scope }).IsUnique();
            e.Property(x => x.Entity).HasMaxLength(64);
            e.Property(x => x.Scope).HasMaxLength(256);
        });

        b.Entity<IdeRegistration>(e =>
        {
            e.HasIndex(x => x.Name).IsUnique();
        });

        b.Entity<KeyValueSetting>(e =>
        {
            e.HasKey(x => x.Key);
            e.Property(x => x.Key).HasMaxLength(128);
        });

        b.Entity<GitlabSettings>(e =>
        {
            e.HasKey(x => x.Id);
        });

        b.Entity<JenkinsSettings>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Username).HasMaxLength(256);
        });

        b.Entity<ApiCallLog>(e =>
        {
            e.HasIndex(x => x.Timestamp);
            e.HasIndex(x => new { x.Source, x.Timestamp });
            e.Property(x => x.Method).HasMaxLength(16);
            e.Property(x => x.Host).HasMaxLength(256);
            e.Property(x => x.Path).HasMaxLength(2048);
            e.Property(x => x.Source).HasMaxLength(32);
        });
    }
}
