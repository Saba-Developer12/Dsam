namespace Dsam.Core.Binary;

public sealed class BinaryAddressMap
{
    private readonly IReadOnlyList<BinarySection> _sections;

    public BinaryAddressMap(IReadOnlyList<BinarySection> sections)
    {
        _sections = sections;
    }

    public bool TryGetSection(ulong virtualAddress, out BinarySection section)
    {
        foreach (var candidate in _sections)
        {
            if (candidate.ContainsVirtualAddress(virtualAddress))
            {
                section = candidate;
                return true;
            }
        }

        section = default!;
        return false;
    }

    public bool TryVirtualAddressToFileOffset(ulong virtualAddress, out long fileOffset)
    {
        if (TryGetSection(virtualAddress, out var section))
        {
            return section.TryVirtualAddressToFileOffset(virtualAddress, out fileOffset);
        }

        fileOffset = 0;
        return false;
    }
}
