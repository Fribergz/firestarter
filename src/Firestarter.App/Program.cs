using Firestarter.App.Ipc;
using Firestarter.App.Web;
using Firestarter.Core.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Photino.NET;
using System.Net.Sockets;

namespace Firestarter.App;

public static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        var builder = Host.CreateApplicationBuilder(args);
        builder.Logging.SetMinimumLevel(LogLevel.Information);

        builder.Services.Bootstrap();

        using var host = builder.Build();

        using (var scope = host.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FirestarterDbContext>();
            db.Database.Migrate();
        }

        host.StartAsync().GetAwaiter().GetResult();

        FirestarterSystemTray? systemTray = null;
        try
        {
            var dispatcher = host.Services.GetRequiredService<IpcDispatcher>();
            var (url, useEmbedded) = ResolveStartUrl(builder.Environment);
            var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "Firestarter.ico");
            if (!File.Exists(iconPath))
                throw new FileNotFoundException("Application icon is missing. Expected: " + iconPath, iconPath);

            var window = new PhotinoWindow()
                .SetTitle("Firestarter")
                .SetIconFile(iconPath)
                .SetUseOsDefaultSize(false)
                .SetUseOsDefaultLocation(false)
                .SetSize(1560, 1024)
                .Center()
                .SetChromeless(true)
                .SetContextMenuEnabled(false)
                .SetNotificationRegistrationId("2F0E8B5C-0D4A-4B7E-8C3D-A1B2C3D4E5F6")
                .SetNotificationsEnabled(false)
                .RegisterWebMessageReceivedHandler((sender, message) => dispatcher.Dispatch(message));

            if (useEmbedded)
                window.RegisterCustomSchemeHandler("app", EmbeddedWebAssets.Handle);

            window.StartUrl = url;

            window.WindowCreatedHandler += (_, _) =>
            {
                systemTray = new FirestarterSystemTray(window);
                systemTray.OnWindowCreated(new Icon(iconPath));
            };

            dispatcher.BindWindow(window);
            host.Services.GetRequiredService<WindowAccessor>().Bind(window);

            window.WaitForClose();
        }
        finally
        {
            systemTray?.Dispose();
            host.StopAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
        }
        return 0;
    }

    static (string Url, bool UseEmbedded) ResolveStartUrl(IHostEnvironment env)
    {
        const string DevUrl = "http://127.0.0.1:5173/";
        var embedded = Path.Combine(AppContext.BaseDirectory, "wwwroot", "index.html");
        var embeddedExists = File.Exists(embedded);

        if (env.IsDevelopment() && IsViteListening())
            return (DevUrl, false);

        if (embeddedExists)
            return (EmbeddedWebAssets.StartUrl, true);

        return (DevUrl, false);
    }

    static bool IsViteListening()
    {
        try
        {
            using var client = new TcpClient();
            var connect = client.BeginConnect("127.0.0.1", 5173, null, null);
            return connect.AsyncWaitHandle.WaitOne(TimeSpan.FromMilliseconds(250)) && client.Connected;
        }
        catch
        {
            return false;
        }
    }
}
