using System;
using System.IO;
using System.Runtime.InteropServices;

namespace ExpandableAllocator
{
    /// <summary>
    ///   Represents an expandable <see cref="Stream" /> that uses an <see cref="ExpandableAllocator.Allocator" />
    ///   to grow in memory when needed.
    /// </summary>
    public sealed class ExpandableStream : Stream
    {
        private long length;
        private long position;
        private readonly long maxLength;
        private readonly bool disposeUnderlying;

        /// <summary>
        ///   Gets the underlying <see cref="ExpandableAllocator.Allocator" />.
        /// </summary>
        public Allocator Allocator { get; }

        /// <summary>
        ///   Creates a new expandable stream, given an underlying allocator.
        /// </summary>
        public ExpandableStream(Allocator allocator, bool disposeAllocator = false)
        {
            Allocator = allocator ?? throw new ArgumentNullException(nameof(allocator));
            maxLength = allocator.Address.ToInt64();
            position = allocator.ActualSize.ToInt64();
            disposeUnderlying = disposeAllocator;
        }

        /// <inheritdoc />
        public override bool CanRead => Allocator.Protection.HasFlag(Protection.Read);

        /// <inheritdoc />
        public override bool CanWrite => Allocator.Protection.HasFlag(Protection.Write);

        /// <inheritdoc />
        public override bool CanSeek => true;

        /// <inheritdoc />
        public override long Length => length;

        /// <inheritdoc />
        public override long Position
        {
            get => position;
            set
            {
                if (value >= length || value < 0)
                    throw new ArgumentOutOfRangeException(nameof(value));

                position = value;
            }
        }

        /// <inheritdoc />
        public override long Seek(long offset, SeekOrigin origin)
        {
            switch (origin)
            {
                case SeekOrigin.Begin:
                    if (offset < 0 || offset >= length)
                        throw new ArgumentOutOfRangeException(nameof(offset));
                    position = offset;
                    break;

                case SeekOrigin.Current:
                    offset += position;
                    goto case SeekOrigin.Begin;

                case SeekOrigin.End:
                    if (offset > 0 || length - offset < 0)
                        throw new ArgumentOutOfRangeException(nameof(offset));
                    position = length + offset;
                    break;
            }

            return position;
        }

        /// <inheritdoc />
        public override void SetLength(long value)
        {
            if (value < 0 || value > Allocator.MaximumSize.ToInt64())
                throw new ArgumentOutOfRangeException(nameof(value));

            Allocator.ActualSize = new IntPtr(value);
            length = value;
        }

        /// <inheritdoc />
        public override void Write(byte[] buffer, int offset, int count)
        {
            long neededLength = position + offset + count;

            if (!CanWrite || neededLength >= maxLength)
                throw new InvalidOperationException();
            if (neededLength > length)
            {
                length = neededLength;
                Allocator.ActualSize = new IntPtr(neededLength);
            }

            IntPtr addr = new IntPtr(Allocator.Address.ToInt64() + position + offset);

            Marshal.Copy(buffer, 0, addr, count);
            position += count;
        }

        /// <inheritdoc />
        public override void WriteByte(byte value)
        {
            if (!CanWrite || position == maxLength)
                throw new InvalidOperationException();

            if (position == length)
            {
                long newSize = Allocator.ActualSize.ToInt64() * 2;

                if (newSize > maxLength)
                    newSize = maxLength;

                Allocator.ActualSize = new IntPtr(newSize);
            }
            
            Marshal.WriteByte(Allocator.Address, (int)position++, value);
            length++;
        }

        /// <inheritdoc />
        public override int Read(byte[] buffer, int offset, int count)
        {
            if (!CanRead)
                throw new InvalidOperationException();
            if (position + offset >= Length)
                return 0;
            
            int toCopy = (int)(length - position - offset);

            if (toCopy > buffer.Length)
                toCopy = buffer.Length;
            if (toCopy > count)
                toCopy = count;

            IntPtr addr = new IntPtr(Allocator.Address.ToInt64() + position + offset);

            if (addr.ToInt64() <= 0)
                throw new InvalidOperationException();

            Marshal.Copy(addr, buffer, 0, toCopy);

            position += toCopy;

            return toCopy;
        }

        /// <inheritdoc />
        public override int ReadByte()
        {
            if (!CanRead)
                throw new InvalidOperationException();
            if (position == length)
                return -1;

            return Marshal.ReadByte(Allocator.Address, (int)position++);
        }

        /// <summary>
        ///   Does nothing.
        /// </summary>
        public override void Flush()
        {
        }

        /// <inheritdoc />
        protected override void Dispose(bool disposing)
        {
            if (disposeUnderlying)
                Allocator.Dispose();
        }
    }
}