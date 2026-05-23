namespace Dsam.Core.Recompilation;

public sealed record PatchPlan(
    ulong Address,
    long FileOffset,
    byte[] OriginalBytes,
    byte[] PatchedBytes);

public sealed record PatchPlanResult(
    bool Succeeded,
    PatchPlan? Plan,
    string? ErrorMessage)
{
    public static PatchPlanResult Success(PatchPlan plan) => new(true, plan, null);

    public static PatchPlanResult Failure(string errorMessage) => new(false, null, errorMessage);
}
