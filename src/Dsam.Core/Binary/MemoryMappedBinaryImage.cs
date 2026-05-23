using System.IO.MemoryMappedFiles;

namespace Dsam.Core.Binary;

public sealed class MemoryMappedBinaryImage : IBinaryImage
{
    private readonly FileStream _stream;
    private readonly MemoryMappedFile _mappedFile;
    private bool _disposed;

    private MemoryMappedBinaryImage(string filePath, FileStream stream, MemoryMappedFile mappedFile)
    {
        FilePath = filePath;
        _stream = stream;
        _mappedFile = mappedFile;
        Length = stream.Length;
    }

    public string FilePath { get; }

    public long Length { get; }

    public static MemoryMappedBinaryImage Open(string filePath)
    {
        var fullPath = Path.GetFullPath(filePath);
        var stream = new FileStream(
            fullPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            bufferSize: 1,
            FileOptions.RandomAccess);

        var mappedFile = MemoryMappedFile.CreateFromFile(
            stream,
            mapName: null,
            capacity: 0,
            MemoryMappedFileAccess.Read,
            HandleInheritability.None,
            leaveOpen: true);

        return new MemoryMappedBinaryImage(fullPath, stream, mappedFile);
    }

    public void Read(long fileOffset, Span<byte> destination)
    {
        ThrowIfDisposed();
        ValidateRange(fileOffset, destination.Length);

        using var stream = _mappedFile.CreateViewStream(fileOffset, destination.Length, MemoryMappedFileAccess.Read);
        var totalRead = 0;
        while (totalRead < destination.Length)
        {
            var read = stream.Read(destination[totalRead..]);
            if (read == 0)
            {
                throw new EndOfStreamException($"Could not read {destination.Length} bytes from offset 0x{fileOffset:X}.");
            }

            totalRead += read;
        }
    }

    public byte[] ReadBytes(long fileOffset, int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);

        var buffer = new byte[count];
        Read(fileOffset, buffer);
        return buffer;
    }

    public Stream OpenReadStream(long fileOffset, long length)
    {
        ThrowIfDisposed();
        ValidateRange(fileOffset, length);
        return _mappedFile.CreateViewStream(fileOffset, length, MemoryMappedFileAccess.Read);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _mappedFile.Dispose();
        _stream.Dispose();
        _disposed = true;
    }

    private void ValidateRange(long fileOffset, long count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(fileOffset);
        ArgumentOutOfRangeException.ThrowIfNegative(count);

        if (fileOffset > Length || count > Length - fileOffset)
        {
            throw new ArgumentOutOfRangeException(nameof(fileOffset), $"Range 0x{fileOffset:X}+0x{count:X} is outside {FilePath}.");
        }
    }

    private void ThrowIfDisposed() =>
        ObjectDisposedException.ThrowIf(_disposed, this);
}
