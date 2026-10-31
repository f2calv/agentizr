namespace CasCap.Client.Layout;

/// <summary>Renders host navigation without exposing unpublished editor dependencies in Release.</summary>
public partial class MainLayout
{
#if DEBUG
    private static bool RuntimeLoggingAvailable => true;
#else
    private static bool RuntimeLoggingAvailable => false;
#endif
}
