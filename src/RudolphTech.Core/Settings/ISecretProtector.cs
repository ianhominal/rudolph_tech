using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;

namespace RudolphTech.Core.Settings;

/// <summary> Turns a secret into something safe to write to disk, and back. </summary>
public interface ISecretProtector
{
    string Protect(string value);

    /// <summary> Null when the stored value cannot be read back (copied from another user or machine). </summary>
    string? Unprotect(string value);
}

/// <summary>
/// Windows DPAPI scoped to the current user: the protected value can only be read back by the same
/// Windows account on the same machine, which is exactly the office PC account the survey runs in.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class DpapiSecretProtector : ISecretProtector
{
    /// <summary> Extra entropy, so a settings.json copied elsewhere cannot be decrypted by another app of ours. </summary>
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("RudolphTech.settings.v1");

    public string Protect(string value) =>
        Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(value), Entropy, DataProtectionScope.CurrentUser));

    public string? Unprotect(string value)
    {
        try
        {
            return Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(value), Entropy, DataProtectionScope.CurrentUser));
        }
        catch (Exception exception) when (exception is CryptographicException or FormatException)
        {
            return null;
        }
    }
}
