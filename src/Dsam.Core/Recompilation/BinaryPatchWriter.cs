namespace Dsam.Core.Recompilation;

public static class BinaryPatchWriter
{
    public static async Task ApplyToCopyAsync(
        string sourcePath,
        string destinationPath,
        IEnumerable<PatchPlan> patches,
        CancellationToken cancellationToken = default)
    {
        File.Copy(sourcePath, destinationPath, overwrite: false);

        await using var output = new FileStream(
            destinationPath,
            FileMode.Open,
            FileAccess.ReadWrite,
            FileShare.None,
            bufferSize: 4096,
            FileOptions.RandomAccess | FileOptions.Asynchronous);

        foreach (var patch in patches.OrderBy(patch => patch.FileOffset))
        {
            cancellationToken.ThrowIfCancellationRequested();
            output.Seek(patch.FileOffset, SeekOrigin.Begin);
            await output.WriteAsync(patch.PatchedBytes, cancellationToken);
        }
    }
}
