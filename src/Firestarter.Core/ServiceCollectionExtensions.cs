using Firestarter.Core.Data;
using Firestarter.Core.Dotnet;
using Firestarter.Core.Extensions;
using Firestarter.Core.Git;
using Firestarter.Core.GitLab;
using Firestarter.Core.HttpTracking;
using Firestarter.Core.Ide;
using Firestarter.Core.MergeRequests;
using Firestarter.Core.Projects;
using Firestarter.Core.Search;
using Firestarter.Core.Security;
using Firestarter.Core.Settings;
using Firestarter.Core.Stats;
using Firestarter.Core.Sync;
using Firestarter.Core.Updates;
using Firestarter.Core.Workspaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Firestarter.Core;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddFirestarterCore(this IServiceCollection services)
    {
        AppPaths.EnsureCreated();

        services.AddDbContext<FirestarterDbContext>(options =>
        {
            options.UseSqlite($"Data Source={AppPaths.DatabasePath}");
        });

        services.AddSingleton<ICredentialStore, WindowsCredentialStore>();
        services.AddScoped<SettingsService>();

        services.AddScoped<IGitLabClientFactory, GitLabClientFactory>();
        services.AddScoped<MergeRequestTabService>();
        services.AddScoped<ProjectSearchService>();
        services.AddScoped<ProjectReadService>();
        services.AddScoped<ProjectPipelineService>();
        services.AddScoped<ProjectFieldService>();
        services.AddScoped<WorkspaceSettings>();
        services.AddScoped<UiSettings>();
        services.AddSingleton<GitCli>();
        services.AddScoped<IdeLauncher>();
        services.AddScoped<WorkspaceService>();
        services.AddScoped<ExtensionRegistry>();
        services.AddScoped<ExtensionRunner>();
        services.AddScoped<ExtensionRunHistory>();
        services.AddSingleton<SyncStatusHub>();
        services.AddSingleton<GitLabSyncService>();
        services.AddHostedService(sp => sp.GetRequiredService<GitLabSyncService>());
        services.AddSingleton<InteractiveDotnetRunService>();

        services.AddSingleton<HttpCallRecorder>();
        services.AddHostedService(sp => sp.GetRequiredService<HttpCallRecorder>());
        services.AddSingleton<HttpCallObserver>();
        services.AddHostedService(sp => sp.GetRequiredService<HttpCallObserver>());
        services.AddHostedService<ApiCallLogRetention>();
        services.AddHostedService<ApiCallSourceBackfill>();
        services.AddScoped<ApiCallStatsService>();

        services.AddSingleton<UpdateStatusHub>();
        services.AddHostedService<UpdateCheckService>();
        services.AddSingleton<UpdateInstaller>();

        return services;
    }
}
