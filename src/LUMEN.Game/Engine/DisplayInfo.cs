using System.Runtime.InteropServices;
using Lumen.Core.Diagnostics;

namespace Lumen.Game.Engine;

/// <summary>
/// Queries the monitor's refresh rate through SDL2 (which MonoGame DesktopGL already
/// loads). MonoGame's own <c>DisplayMode</c> does not expose refresh rate, so we ask
/// SDL directly and fall back to a safe default if the call is unavailable.
/// </summary>
public static class DisplayInfo
{
    public const int FallbackRefreshRate = 240;

    [StructLayout(LayoutKind.Sequential)]
    private struct SdlDisplayMode
    {
        public uint Format;
        public int W;
        public int H;
        public int RefreshRate;
        public nint DriverData;
    }

    [DllImport("SDL2", CallingConvention = CallingConvention.Cdecl)]
    private static extern int SDL_GetCurrentDisplayMode(int displayIndex, out SdlDisplayMode mode);

    public static int GetPrimaryRefreshRate()
    {
        try
        {
            if (SDL_GetCurrentDisplayMode(0, out SdlDisplayMode mode) == 0 && mode.RefreshRate > 0)
            {
                return mode.RefreshRate;
            }
        }
        catch (DllNotFoundException) { }
        catch (EntryPointNotFoundException) { }

        Log.Warn($"could not query refresh rate; assuming {FallbackRefreshRate}Hz");
        return FallbackRefreshRate;
    }
}
