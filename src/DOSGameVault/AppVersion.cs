using System.Reflection;
namespace DOSGameVault;
public static class AppVersion
{
    public static string Display => "v" + (typeof(AppVersion).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0] ?? "0.8.3");
}
