using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using GearInvest;

return;

[SupportedOSPlatform("browser")]
public static partial class BrowserRuntime
{
    private static readonly MechanicalRuntimeHost Host = new(GearInvestSdk.CreateDefault());
    [JSExport]
    public static string Dispatch(string commandJson) => Host.Dispatch(commandJson);

}
