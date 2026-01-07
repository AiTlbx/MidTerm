namespace Ai.Tlbx.MidTerm.TtyHost;

/// <summary>
/// Fixed-size circular buffer for terminal scrollback.
/// Single allocation at creation, O(1) trim, no GC pressure during writes.
/// </summary>
public sealed class CircularByteBuffer
{
    private readonly byte[] _buffer;
    private int _head;  // next write position
    private int _tail;  // oldest data position
    private int _count; // bytes currently stored

    public int Count => _count;
    public int Capacity => _buffer.Length;

    public CircularByteBuffer(int capacity)
    {
        if (capacity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity), "Capacity must be positive");
        }
        _buffer = new byte[capacity];
    }

    public void Write(ReadOnlySpan<byte> data)
    {
        if (data.Length == 0) return;

        var capacity = _buffer.Length;

        // If data >= capacity, only keep last (capacity) bytes
        if (data.Length >= capacity)
        {
            data.Slice(data.Length - capacity).CopyTo(_buffer);
            _head = 0;
            _tail = 0;
            _count = capacity;
            return;
        }

        // Calculate overflow, advance tail to discard oldest
        var overflow = (_count + data.Length) - capacity;
        if (overflow > 0)
        {
            _tail = (_tail + overflow) % capacity;
            _count -= overflow;
        }

        // Write first chunk (from head to end of buffer or end of data)
        var firstChunk = Math.Min(data.Length, capacity - _head);
        data.Slice(0, firstChunk).CopyTo(_buffer.AsSpan(_head, firstChunk));

        // Write second chunk if wrapped
        var secondChunk = data.Length - firstChunk;
        if (secondChunk > 0)
        {
            data.Slice(firstChunk).CopyTo(_buffer.AsSpan(0, secondChunk));
        }

        _head = (_head + data.Length) % capacity;
        _count += data.Length;
    }

    public byte[] ToArray()
    {
        var result = new byte[_count];
        if (_count == 0) return result;

        if (_tail < _head)
        {
            // Contiguous: [....TAIL####HEAD....]
            Array.Copy(_buffer, _tail, result, 0, _count);
        }
        else
        {
            // Wrapped: [###HEAD.....TAIL####]
            var tailToEnd = _buffer.Length - _tail;
            Array.Copy(_buffer, _tail, result, 0, tailToEnd);
            Array.Copy(_buffer, 0, result, tailToEnd, _head);
        }

        return result;
    }

    public void Clear()
    {
        _head = 0;
        _tail = 0;
        _count = 0;
    }
}
