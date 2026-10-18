namespace FactoryOps.Web.Models;

// Keep the existing Phase 1 choices consistent across creation, editing and validation.
public static class OrderProducts
{
    public static IReadOnlyList<string> Allowed { get; } =
        Array.AsReadOnly(new[] { "Fast Tread", "BlueDeck", "SureFit" });
}
