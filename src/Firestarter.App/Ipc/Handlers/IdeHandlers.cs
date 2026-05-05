using Firestarter.Core.Data.Entities;
using Firestarter.Core.Ide;
using System.Text.Json;

namespace Firestarter.App.Ipc.Handlers;

public class IdeListHandler(IdeLauncher ide) : IIpcHandler
{
    readonly IdeLauncher _ide = ide;

    public async Task<object?> HandleAsync(JsonElement? payload, CancellationToken ct)
    {
        var list = await _ide.ListAsync(ct);
        return new { ides = list.Select(Map).ToArray() };
    }

    internal static object Map(IdeRegistration r) => new
    {
        id = r.Id,
        name = r.Name,
        executablePath = r.ExecutablePath,
        argTemplate = r.ArgTemplate,
        isDefault = r.IsDefault,
    };
}

public class IdeUpsertHandler(IdeLauncher ide) : IIpcHandler
{
    static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    readonly IdeLauncher _ide = ide;

    public async Task<object?> HandleAsync(JsonElement? payload, CancellationToken ct)
    {
        if (payload is null) throw new ArgumentException("payload required");
        var dto = payload.Value.Deserialize<IdePayload>(Options) ?? throw new ArgumentException("invalid payload");
        var saved = await _ide.UpsertAsync(new IdeRegistration
        {
            Id = dto.Id ?? 0,
            Name = dto.Name ?? string.Empty,
            ExecutablePath = dto.ExecutablePath ?? string.Empty,
            ArgTemplate = dto.ArgTemplate ?? "\"{path}\"",
            IsDefault = dto.IsDefault ?? false,
        }, ct);
        return IdeListHandler.Map(saved);
    }

    class IdePayload
    {
        public int? Id { get; set; }
        public string? Name { get; set; }
        public string? ExecutablePath { get; set; }
        public string? ArgTemplate { get; set; }
        public bool? IsDefault { get; set; }
    }
}

public class IdeDeleteHandler(IdeLauncher ide) : IIpcHandler
{
    static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    readonly IdeLauncher _ide = ide;

    public async Task<object?> HandleAsync(JsonElement? payload, CancellationToken ct)
    {
        var dto = payload?.Deserialize<DeletePayload>(Options) ?? throw new ArgumentException("payload required");
        if (dto.Id is null) throw new ArgumentException("id required");
        await _ide.DeleteAsync(dto.Id.Value, ct);
        return new { deleted = true };
    }

    class DeletePayload { public int? Id { get; set; } }
}
