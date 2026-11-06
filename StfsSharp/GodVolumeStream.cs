namespace StfsSharp;

/// <summary>
/// Seekable read-only <see cref="Stream"/> over the logical image exposed by a
/// <see cref="GodVolume"/>. Seeking is supported; writing throws
/// <see cref="NotSupportedException"/>.
/// </summary>
public sealed class GodVolumeStream : Stream
{
    private readonly GodVolume _volume;
    private long _position;
    private bool _disposed;

    internal GodVolumeStream(GodVolume volume)
    {
        _volume = volume;
    }

    /// <inheritdoc/>
    public override bool CanRead => !_disposed;

    /// <inheritdoc/>
    public override bool CanSeek => !_disposed;

    /// <inheritdoc/>
    public override bool CanWrite => false;

    /// <inheritdoc/>
    public override long Length
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _volume.Length;
        }
    }

    /// <inheritdoc/>
    public override long Position
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _position;
        }
        set
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            ArgumentOutOfRangeException.ThrowIfNegative(value);
            _position = value;
        }
    }

    /// <inheritdoc/>
    public override int Read(byte[] buffer, int offset, int count)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        return Read(buffer.AsSpan(offset, count));
    }

    /// <inheritdoc/>
    public override int Read(Span<byte> buffer)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        int read = _volume.Read(_position, buffer);
        _position += read;
        return read;
    }

    /// <inheritdoc/>
    public override int ReadByte()
    {
        Span<byte> one = stackalloc byte[1];
        return Read(one) == 0 ? -1 : one[0];
    }

    /// <inheritdoc/>
    public override long Seek(long offset, SeekOrigin origin)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        long target = origin switch
        {
            SeekOrigin.Begin => offset,
            SeekOrigin.Current => checked(_position + offset),
            SeekOrigin.End => checked(Length + offset),
            _ => throw new ArgumentOutOfRangeException(nameof(origin), origin, "Unknown seek origin."),
        };

        if (target < 0)
        {
            throw new IOException("An attempt was made to seek before the beginning of the stream.");
        }

        _position = target;
        return _position;
    }

    /// <inheritdoc/>
    public override void Flush()
    {
        // Read-only stream: nothing to flush.
    }

    /// <inheritdoc/>
    public override void SetLength(long value) =>
        throw new NotSupportedException("GOD volumes are read-only.");

    /// <inheritdoc/>
    public override void Write(byte[] buffer, int offset, int count) =>
        throw new NotSupportedException("GOD volumes are read-only.");

    /// <inheritdoc/>
    public override void Write(ReadOnlySpan<byte> buffer) =>
        throw new NotSupportedException("GOD volumes are read-only.");

    /// <inheritdoc/>
    protected override void Dispose(bool disposing)
    {
        _disposed = true;
        base.Dispose(disposing);
    }
}
