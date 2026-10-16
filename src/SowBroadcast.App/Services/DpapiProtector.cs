using System.Security.Cryptography;
using System.Text;
using SowBroadcast.Core.Obs;

namespace SowBroadcast.App.Services;

/// <summary>Windows DPAPI for the current user: what is stored cannot be read by another user or on another computer.</summary>
public sealed class DpapiProtector : ISecretProtector
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("SOWBroadcast.Obs");

    public string Protect(string plain) =>
        Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(plain), Entropy, DataProtectionScope.CurrentUser));

    public string? Unprotect(string protectedText)
    {
        try { return Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(protectedText), Entropy, DataProtectionScope.CurrentUser)); }
        catch (Exception ex) when (ex is CryptographicException or FormatException) { return null; }
    }
}
