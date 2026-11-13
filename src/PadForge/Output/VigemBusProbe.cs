using System.Diagnostics;
using Nefarius.ViGEm.Client;
using Nefarius.ViGEm.Client.Exceptions;

namespace PadForge.Output;

public readonly record struct BusProbeResult(bool IsInstalled, string Message);

public static class VigemBusProbe
{
    public static BusProbeResult Check()
    {
        try
        {
            using var client = new ViGEmClient();
            return new BusProbeResult(true, "ViGEmBus is installed.");
        }
        catch (VigemBusNotFoundException)
        {
            return new BusProbeResult(false, "ViGEmBus was not found.");
        }
        catch (DllNotFoundException exception)
        {
            Debug.WriteLine(exception);
            return new BusProbeResult(false, "The ViGEm client library failed to load.");
        }
        catch (Exception exception)
        {
            Debug.WriteLine(exception);
            return new BusProbeResult(false, "Could not open ViGEmBus.");
        }
    }
}
