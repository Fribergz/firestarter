using Firestarter.Core.Settings;
using System.Text.Json;

namespace Firestarter.App.Ipc.Handlers;

public class UiGetHandler(UiSettings ui) : IIpcHandler
{
    readonly UiSettings _ui = ui;

    public async Task<object?> HandleAsync(JsonElement? payload, CancellationToken ct)
    {
        var cfg = await _ui.GetAsync(ct);
        return new { theme = cfg.Theme };
    }
}

public class UiSetThemeHandler(UiSettings ui) : IIpcHandler
{
    static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    readonly UiSettings _ui = ui;

    public async Task<object?> HandleAsync(JsonElement? payload, CancellationToken ct)
    {
        var dto = payload?.Deserialize<ThemeDto>(Options) ?? new ThemeDto();
        var cfg = await _ui.SetThemeAsync(dto.Theme, ct);
        return new { theme = cfg.Theme };
    }

    class ThemeDto { public string? Theme { get; set; } }
}
