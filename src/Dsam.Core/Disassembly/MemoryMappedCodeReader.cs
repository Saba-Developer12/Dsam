using Dsam.Core.Binary;
using Iced.Intel;

namespace Dsam.Core.Disassembly;

internal sealed class MemoryMappedCodeReader : CodeReader
{
    private const int DefaultWindowSize = 64 * 1024;

    private readonly IBinaryImage _image;
    private readonly long _endOffset;
    private readonly byte[] _window;
    private long _position;
    private long _windowStart = -1;
    private int _windowLength;

    public MemoryMappedCodeReader(IBinaryImage image, long startOffset, long length, int windowSize = DefaultWindowSize)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(startOffset);
        ArgumentOutOfRangeException.ThrowIfNegative(length);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(windowSize);

        _image = image;
        _position = startOffset;
        _endOffset = checked(startOffset + length);
        _window = new byte[windowSize];
    }

    public override int ReadByte()
    {
        if (_position >= _endOffset)
        {
            return -1;
        }

        if (_position < _windowStart || _position >= _windowStart + _windowLength)
        {
            FillWindow();
        }

        var value = _window[(int)(_position - _windowStart)];
        _position++;
        return value;
    }

    private void FillWindow()
    {
        _windowStart = _position;
        var remaining = _endOffset - _position;
        _windowLength = (int)Math.Min(_window.Length, remaining);
        _image.Read(_windowStart, _window.AsSpan(0, _windowLength));
    }
}
