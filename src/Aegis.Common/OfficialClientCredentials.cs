namespace Aegis.Common;

/// <summary>
/// Built-in application credentials for the official Aegis client.
/// These values are intentionally code-level constants and are expected to be
/// embedded in first-party clients.
/// </summary>
public static class OfficialClientCredentials
{
    public const int AppId = 2041001;
    public const string AppHash = "8f4c1db0e7c2456d9ab31f4e6d8c9a0137f2c4b56d8e1a903bc7d52e6f194a3c";
    public const string AppTitle = "Aegis Official Client";
    public const string ShortName = "aegis_official";
    public const string Platform = "desktop";
}