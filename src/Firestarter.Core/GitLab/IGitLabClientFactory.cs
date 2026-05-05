using NGitLab;

namespace Firestarter.Core.GitLab;

public interface IGitLabClientFactory
{
    Task<IGitLabClient?> CreateAsync(CancellationToken ct = default);
}
