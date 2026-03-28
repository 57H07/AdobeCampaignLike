namespace CampaignEngine.Infrastructure.DataSources;

/// <summary>
/// Wraps a stream and throws <see cref="LimitExceededException"/> if the
/// number of bytes read exceeds the configured maximum.
///
/// Used by RestApiConnector to enforce the 50 MB response size business rule (US-017).
/// </summary>
internal sealed class LimitedStream : Stream
{
    private readonly Stream _inner;
    private readonly long _maxBytes;
    private long _bytesRead;

    public LimitedStream(Stream inner, long maxBytes)
    {
        _inner = inner;
        _maxBytes = maxBytes;
    }

    public override bool CanRead => _inner.CanRead;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position
    {
        get => _inner.Position;
        set => throw new NotSupportedException();
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        var bytesRead = _inner.Read(buffer, offset, count);
        CheckLimit(bytesRead);
        return bytesRead;
    }

    public override async Task<int> ReadAsync(
        byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        var bytesRead = await _inner.ReadAsync(buffer, offset, count, cancellationToken);
        CheckLimit(bytesRead);
        return bytesRead;
    }

    public override async ValueTask<int> ReadAsync(
        Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        var bytesRead = await _inner.ReadAsync(buffer, cancellationToken);
        CheckLimit(bytesRead);
        return bytesRead;
    }

    private void CheckLimit(int bytesRead)
    {
        _bytesRead += bytesRead;
        if (_bytesRead > _maxBytes)
            throw new LimitExceededException(_maxBytes);
    }

    public override void Flush() => _inner.Flush();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing) _inner.Dispose();
        base.Dispose(disposing);
    }

    /// <summary>Thrown when the response body exceeds the configured size limit.</summary>
    public sealed class LimitExceededException : IOException
    {
        public LimitExceededException(long limitBytes)
            : base($"Response size limit of {limitBytes / (1024 * 1024)} MB exceeded.")
        {
        }
    }
}
