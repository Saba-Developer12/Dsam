namespace Dsam.Core.Binary;

public interface IBinaryImage : IDisposable
{
    string FilePath { get; }

    long Length { get; }

    void Read(long fileOffset, Span<byte> destination);

    byte[] ReadBytes(long fileOffset, int count);

    Stream OpenReadStream(long fileOffset, long length);
}
