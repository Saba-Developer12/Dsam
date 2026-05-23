namespace Dsam.Core.Binary;

public sealed record BinarySection(
    string Name,
    ulong VirtualAddress,
    uint VirtualSize,
    long FileOffset,
    long FileSize,
    bool IsExecutable,
    bool ContainsCode)
{
    public ulong EndVirtualAddress => VirtualAddress + Math.Max(VirtualSize, (uint)Math.Max(FileSize, 0));

    public bool ContainsVirtualAddress(ulong address) =>
        address >= VirtualAddress && address < EndVirtualAddress;

    public bool TryVirtualAddressToFileOffset(ulong address, out long fileOffset)
    {
        if (!ContainsVirtualAddress(address))
        {
            fileOffset = 0;
            return false;
        }

        var sectionDelta = address - VirtualAddress;
        if (sectionDelta > (ulong)Math.Max(FileSize, 0))
        {
            fileOffset = 0;
            return false;
        }

        fileOffset = checked(FileOffset + (long)sectionDelta);
        return true;
    }
}
