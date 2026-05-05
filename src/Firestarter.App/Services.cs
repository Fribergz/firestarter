using Firestarter.App.Ipc;
using Firestarter.App.Ipc.Handlers;
using Firestarter.Core;
using Microsoft.Extensions.DependencyInjection;

namespace Firestarter.App;

public static class Services
{
    public static IServiceCollection Bootstrap(this IServiceCollection services)
    {
        services.AddFirestarterCore();
        services.AddSingleton<IpcDispatcher>();
        services.AddSingleton<WindowAccessor>();
        services.AddKeyedScoped<IIpcHandler, PingHandler>("ping");
        services.AddKeyedScoped<IIpcHandler, WindowMinimizeHandler>("window.minimize");
        services.AddKeyedScoped<IIpcHandler, WindowToggleMaximizeHandler>("window.toggleMaximize");
        services.AddKeyedScoped<IIpcHandler, WindowCloseHandler>("window.close");
        services.AddKeyedScoped<IIpcHandler, WindowHideToTrayHandler>("window.hideToTray");
        services.AddKeyedScoped<IIpcHandler, WindowStateHandler>("window.state");
        services.AddKeyedScoped<IIpcHandler, WindowStartDragHandler>("window.startDrag");
        services.AddKeyedScoped<IIpcHandler, WindowStartResizeHandler>("window.startResize");
        services.AddKeyedScoped<IIpcHandler, SettingsGetHandler>("settings.get");
        services.AddKeyedScoped<IIpcHandler, SettingsUpdateHandler>("settings.update");
        services.AddKeyedScoped<IIpcHandler, JenkinsGetHandler>("jenkins.get");
        services.AddKeyedScoped<IIpcHandler, JenkinsUpdateHandler>("jenkins.update");
        services.AddKeyedScoped<IIpcHandler, JenkinsTestHandler>("jenkins.test");
        services.AddKeyedScoped<IIpcHandler, JenkinsSetProjectJobHandler>("jenkins.projectJob.set");
        services.AddKeyedScoped<IIpcHandler, JenkinsBuildStatusHandler>("jenkins.build.status");
        services.AddKeyedScoped<IIpcHandler, JenkinsBuildTriggerHandler>("jenkins.build.trigger");
        services.AddKeyedScoped<IIpcHandler, SyncStartHandler>("sync.start");
        services.AddKeyedScoped<IIpcHandler, SyncStatusHandler>("sync.status");
        services.AddKeyedScoped<IIpcHandler, SyncResetJenkinsJobPathsHandler>("sync.resetJenkinsJobPaths");
        services.AddKeyedScoped<IIpcHandler, ProjectSearchHandler>("projects.search");
        services.AddKeyedScoped<IIpcHandler, ProjectOpenHandler>("projects.open");
        services.AddKeyedScoped<IIpcHandler, ProjectOpenExplorerHandler>("projects.openExplorer");
        services.AddKeyedScoped<IIpcHandler, ProjectOpenTerminalHandler>("projects.openTerminal");
        services.AddKeyedScoped<IIpcHandler, IdeListHandler>("ide.list");
        services.AddKeyedScoped<IIpcHandler, IdeUpsertHandler>("ide.upsert");
        services.AddKeyedScoped<IIpcHandler, IdeDeleteHandler>("ide.delete");
        services.AddKeyedScoped<IIpcHandler, WorkspaceGetHandler>("workspace.get");
        services.AddKeyedScoped<IIpcHandler, WorkspaceUpdateHandler>("workspace.update");
        services.AddKeyedScoped<IIpcHandler, ExtensionsRootGetHandler>("extensions.root.get");
        services.AddKeyedScoped<IIpcHandler, ExtensionsRootSetHandler>("extensions.root.set");
        services.AddKeyedScoped<IIpcHandler, ExtensionsScanHandler>("extensions.scan");
        services.AddKeyedScoped<IIpcHandler, ExtensionsListHandler>("extensions.list");
        services.AddKeyedScoped<IIpcHandler, ExtensionsSetEnabledHandler>("extensions.setEnabled");
        services.AddKeyedScoped<IIpcHandler, ExtensionsSetSettingsHandler>("extensions.setSettings");
        services.AddKeyedScoped<IIpcHandler, ExtensionsRunHandler>("extensions.run");
        services.AddKeyedScoped<IIpcHandler, ExtensionRunsListHandler>("extensions.runs");
        services.AddKeyedScoped<IIpcHandler, ExtensionRunLogHandler>("extensions.runLog");
        services.AddKeyedScoped<IIpcHandler, UiGetHandler>("ui.get");
        services.AddKeyedScoped<IIpcHandler, UiSetThemeHandler>("ui.setTheme");
        services.AddKeyedScoped<IIpcHandler, ProjectsListHandler>("projects.list");
        services.AddKeyedScoped<IIpcHandler, ProjectGetHandler>("projects.get");
        services.AddKeyedScoped<IIpcHandler, ProjectPipelinesCachedListHandler>("projects.pipelines.cached");
        services.AddKeyedScoped<IIpcHandler, ProjectPipelinesListHandler>("projects.pipelines.list");
        services.AddKeyedScoped<IIpcHandler, ProjectMarkVisitedHandler>("projects.markVisited");
        services.AddKeyedScoped<IIpcHandler, ProjectSetStarredHandler>("projects.setStarred");
        services.AddKeyedScoped<IIpcHandler, MergeRequestGetHandler>("mr.get");
        services.AddKeyedScoped<IIpcHandler, MergeRequestOverviewGetHandler>("mr.overview.get");
        services.AddKeyedScoped<IIpcHandler, MergeRequestCommitsGetHandler>("mr.commits.get");
        services.AddKeyedScoped<IIpcHandler, MergeRequestChangesGetHandler>("mr.changes.get");
        services.AddKeyedScoped<IIpcHandler, MergeRequestDiscussionsGetHandler>("mr.discussions.get");
        services.AddKeyedScoped<IIpcHandler, MergeRequestDiscussionCreateHandler>("mr.discussion.create");
        services.AddKeyedScoped<IIpcHandler, MergeRequestsListMineHandler>("mr.listMine");
        services.AddKeyedScoped<IIpcHandler, MergeRequestsListReviewerHandler>("mr.listReviewer");
        services.AddKeyedScoped<IIpcHandler, MergeRequestApproveHandler>("mr.approve");
        services.AddKeyedScoped<IIpcHandler, DotnetInteractiveRunStartHandler>("dotnet.interactiveRun.start");
        services.AddKeyedScoped<IIpcHandler, DotnetInteractiveRunStopHandler>("dotnet.interactiveRun.stop");
        services.AddKeyedScoped<IIpcHandler, DotnetInteractiveRunStatusHandler>("dotnet.interactiveRun.status");
        services.AddKeyedScoped<IIpcHandler, CountersHandler>("counters");
        services.AddKeyedScoped<IIpcHandler, StatsSummaryHandler>("stats.summary");
        services.AddKeyedScoped<IIpcHandler, StatsListHandler>("stats.list");
        services.AddKeyedScoped<IIpcHandler, UpdateStatusHandler>("update.status");
        services.AddKeyedScoped<IIpcHandler, UpdateApplyHandler>("update.apply");

        return services;
    }
}
