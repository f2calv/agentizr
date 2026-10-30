#if DEBUG
namespace CasCap.Models;

/// <summary>Payload-free native-vector validation projection.</summary>
internal sealed record DocumentVectorReceipt(int Ordinal, string InputSha256, int Dimensions);
#endif
