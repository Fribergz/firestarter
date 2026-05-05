using Meziantou.Framework.Win32;
using System.Runtime.Versioning;

namespace Firestarter.Core.Security;

public interface ICredentialStore
{
    string? Get(string name);
    void Set(string name, string secret);
    void Delete(string name);
    bool Exists(string name);
}

[SupportedOSPlatform("windows")]
public class WindowsCredentialStore : ICredentialStore
{
    public string? Get(string name)
    {
        var cred = CredentialManager.ReadCredential(name);
        return cred?.Password;
    }

    public void Set(string name, string secret)
    {
        CredentialManager.WriteCredential(
            applicationName: name,
            userName: "firestarter",
            secret: secret,
            persistence: CredentialPersistence.LocalMachine);
    }

    public void Delete(string name)
    {
        if (Exists(name))
            CredentialManager.DeleteCredential(name);
    }

    public bool Exists(string name) => CredentialManager.ReadCredential(name) is not null;
}
