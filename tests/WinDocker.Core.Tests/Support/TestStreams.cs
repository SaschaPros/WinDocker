using System.Buffers.Binary;
using System.Text;

namespace WinDocker.Core.Tests.Support;

internal static class TestStreams
{
    /// <summary>Builds the bytes of a multiplexed Docker stream: an 8 byte header (stream type, length) per frame.</summary>
    public static byte[] Frames(params (byte Type, string Text)[] frames)
    {
        using var buffer = new MemoryStream();
        foreach (var (type, text) in frames)
        {
            var payload = Encoding.UTF8.GetBytes(text);
            var header = new byte[8];
            header[0] = type;
            BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(4), (uint)payload.Length);
            buffer.Write(header);
            buffer.Write(payload);
        }

        return buffer.ToArray();
    }

    public const byte Stdout = 1;

    public const byte Stderr = 2;
}

/// <summary>
/// A stream that blocks in <see cref="ReadAsync(byte[], int, int, CancellationToken)"/> until it is disposed, and
/// deliberately ignores the cancellation token: like a pending named pipe read that only disposal unblocks.
/// </summary>
internal sealed class BlockingStream : Stream
{
    private readonly byte[] initial;
    private readonly TaskCompletionSource disposed = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource blocked = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int position;

    public BlockingStream(byte[]? initial = null) => this.initial = initial ?? [];

    public bool IsDisposed => disposed.Task.IsCompleted;

    /// <summary>Completes once a read is waiting for more data.</summary>
    public Task Blocked => blocked.Task;

    public override bool CanRead => true;

    public override bool CanSeek => false;

    public override bool CanWrite => false;

    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        if (position < initial.Length)
        {
            var read = Math.Min(count, initial.Length - position);
            Array.Copy(initial, position, buffer, offset, read);
            position += read;
            return read;
        }

        blocked.TrySetResult();
        await disposed.Task;
        throw new ObjectDisposedException(nameof(BlockingStream));
    }

    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    public override void Flush()
    {
    }

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            disposed.TrySetResult();
        }

        base.Dispose(disposing);
    }
}

/// <summary>Serves the given bytes and then fails, like a connection that breaks in the middle of a stream.</summary>
internal sealed class FailingStream(byte[] initial, Exception failure) : Stream
{
    private int position;

    public override bool CanRead => true;

    public override bool CanSeek => false;

    public override bool CanWrite => false;

    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        if (position >= initial.Length)
        {
            return Task.FromException<int>(failure);
        }

        var read = Math.Min(count, initial.Length - position);
        Array.Copy(initial, position, buffer, offset, read);
        position += read;
        return Task.FromResult(read);
    }

    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    public override void Flush()
    {
    }

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}

/// <summary>A <see cref="MemoryStream"/> that remembers whether it was disposed.</summary>
internal sealed class TrackedMemoryStream(byte[] buffer) : MemoryStream(buffer)
{
    public bool WasDisposed { get; private set; }

    protected override void Dispose(bool disposing)
    {
        WasDisposed = true;
        base.Dispose(disposing);
    }
}
