using System;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace ExpandableAllocator
{
    /// <summary>
    ///   Represents the protection of a reserved or commited memory block.
    /// </summary>
    public enum Protection
    {
        /// <summary>Permission to read.</summary>
        Read = 0x1,
        /// <summary>Permission to write.</summary>
        Write = 0x2,
        /// <summary>Permission to execute.</summary>
        Execute = 0x4,

        /// <summary>Permission to read and write.</summary>
        ReadWrite = Read | Write,
        /// <summary>Permission to read and execute.</summary>
        ReadExecute = Read | Execute,
        /// <summary>Permission to read, write and execute.</summary>
        ReadWriteExecute = Read | Write | Execute
    }

    internal static class ProtectionHelpers
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int GetWindowsValue(this Protection prot)
        {
            switch (prot)
            {
                case Protection.Read: return 0x02;
                case Protection.Execute: return 0x10;
                case Protection.ReadWrite: return 0x04;
                case Protection.ReadExecute: return 0x20;
                case Protection.ReadWriteExecute: return 0x40;

                default:
                    throw new ArgumentOutOfRangeException(nameof(prot), "Invalid protection.");
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int GetUnixValue(this Protection prot) => (int) prot;
    }

    /// <summary>
    ///   Defines an allocator whose memory never moves and may grow until a given amount.
    /// </summary>
    public sealed class Allocator : IDisposable
    {
        private IntPtr size;
        private Protection prot;

        private Allocator(IntPtr addr, IntPtr maxSize, Protection protection)
        {
            Address = addr;
            MaximumSize = maxSize;

            prot = protection;
        }

        /// <summary>
        ///   Gets a pointer to the start of the allocated memory region.
        /// </summary>
        public IntPtr Address { get; }

        /// <summary>
        ///   Gets the maximum size of memory that can be allocated.
        /// </summary>
        public IntPtr MaximumSize { get; }

        /// <summary>
        ///   Gets or sets the protection of the reserved region.
        /// </summary>
        public Protection Protection
        {
            get => prot;
            
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set
            {
                bool ok;
                OS os = Foreign.CurrentOS;

                switch (os)
                {
                    case OS.Windows:
                        ok = Foreign.Windows.VirtualProtect(Address, size, value.GetWindowsValue(), out int _);
                        break;

                    case OS.OSX:
                        ok = Foreign.OSX.mprotect(Address, size, value.GetUnixValue()) == 0;
                        break;

                    case OS.Unix:
                        ok = Foreign.Unix.mprotect(Address, size, value.GetUnixValue()) == 0;
                        break;

                    default:
                        // This cannot happen.
                        ok = false;
                        break;
                }

                if (ok)
                    prot = value;
                else
                    throw new IOException($"Unable to change memory protection: {Marshal.GetLastWin32Error()}.");
            }
        }

        /// <summary>
        ///   Gets or sets the actual size of the allocated memory.
        /// </summary>
        public IntPtr ActualSize
        {
            get => size;
            
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set
            {
                if (!TryReserve(value))
                    throw new IOException($"Unable to change actual size of allocated memory: {Marshal.GetLastWin32Error()}.");
            }
        }

        /// <summary>
        ///   Attempts to reserve the given amount of memory.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryReserve(long size) => TryReserve(new IntPtr(size));

        /// <summary>
        ///   Attempts to reserve the given amount of memory.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryReserve(IntPtr size)
        {
            IntPtr actualSize = this.size;

            long sizeL = size.ToInt64();

            if (sizeL <= actualSize.ToInt64())
                return true;
            if (sizeL > MaximumSize.ToInt64())
                return false;

            OS os = Foreign.CurrentOS;

            switch (os)
            {
                case OS.Windows:
                    IntPtr addr = Foreign.Windows.VirtualAlloc(Address, size, 0x00001000 /* MEM_RESERVE */, prot.GetWindowsValue());
                    
                    if (addr == IntPtr.Zero)
                        return false;

                    break;

                default:
                    int status = os == OS.OSX
                               ? Foreign.OSX.mprotect(Address, size, prot.GetUnixValue())
                               : Foreign.Unix.mprotect(Address, size, prot.GetUnixValue());

                    if (status != 0)
                        return false;

                    break;
            }

            this.size = size;
            return true;
        }

        /// <summary>
        ///   Disposes of the allocator, freeing all its reserved and allocated memory.
        /// </summary>
        public void Dispose()
        {
            switch (Foreign.CurrentOS)
            {
                case OS.Windows:
                    Foreign.Windows.VirtualFree(Address, size, 0x8000 /* MEM_RELEASE */);
                    break;
                case OS.Unix:
                    Foreign.Unix.munmap(Address, size);
                    break;
                case OS.OSX:
                    Foreign.OSX.munmap(Address, size);
                    break;
            }
        }


        /// <summary>
        ///   Attempts to create a new allocator, given the protection of the memory
        ///   region and its maximum size.
        /// </summary>
        /// <param name="protection">
        ///   The protection to apply to the allocated region.
        /// </param>
        /// <param name="maxSize">
        ///   The maximum size that can be allocated through the allocator.
        ///   This size may be greater than the available RAM.
        /// </param>
        /// <param name="allocator">
        ///   On success, an allocator that can be used to reserve memory.
        /// </param>
        /// <returns>
        ///   Whether an allocator was successfully created.
        /// </returns>
        public static bool TryCreate(Protection protection, IntPtr maxSize, out Allocator allocator)
        {
            allocator = null;

            if (maxSize == IntPtr.Zero)
                return false;

            OS os = Foreign.CurrentOS;
            IntPtr addr;

            switch (os)
            {
                case OS.Windows:
                    addr = Foreign.Windows.VirtualAlloc(IntPtr.Zero, maxSize,
                                                        0x00002000 /* MEM_RESERVE */,
                                                        protection.GetWindowsValue());
                    break;

                default:
                    addr = os == OS.OSX
                         ? Foreign.OSX.mmap(IntPtr.Zero, maxSize, 0x0 /* PROT_NONE */, 0x22 /* MAP_ANONYMOUS | MAP_PRIVATE */, -1, IntPtr.Zero)
                         : Foreign.Unix.mmap(IntPtr.Zero, maxSize, 0x0, 0x22, -1, IntPtr.Zero);
                    break;
            }

            if (addr == IntPtr.Zero)
                return false;

            allocator = new Allocator(addr, maxSize, protection);
            return true;
        }

        /// <summary>
        ///   Attempts to create a new allocator, given the protection of the memory
        ///   region and its maximum size.
        /// </summary>
        /// <param name="protection">
        ///   The protection to apply to the allocated region.
        /// </param>
        /// <param name="maxSize">
        ///   The maximum size that can be allocated through the allocator.
        ///   This size may be greater than the available RAM.
        /// </param>
        /// <param name="allocator">
        ///   On success, an allocator that can be used to reserve memory.
        /// </param>
        /// <returns>
        ///   Whether an allocator was successfully created.
        /// </returns>
        public static bool TryCreate(Protection protection, long maxSize, out Allocator allocator)
            => TryCreate(protection, new IntPtr(maxSize), out allocator);

        /// <summary>
        ///   Creates a new allocator, given the protection of the memory
        ///   region and its maximum size.
        /// </summary>
        /// <param name="protection">
        ///   The protection to apply to the allocated region.
        /// </param>
        /// <param name="maxSize">
        ///   The maximum size that can be allocated through the allocator.
        ///   This size may be greater than the available RAM.
        /// </param>
        /// <returns>
        ///   An allocator that can be used the reserved memory.
        /// </returns>
        /// <exception cref="IOException"/>
        public static Allocator Create(Protection protection, IntPtr maxSize)
        {
            if (TryCreate(protection, maxSize, out Allocator allocator))
                return allocator;
            
            throw new IOException($"Could not create an allocator: {Marshal.GetLastWin32Error()}.");
        }

        /// <summary>
        ///   Creates a new allocator, given the protection of the memory
        ///   region and its maximum size.
        /// </summary>
        /// <param name="protection">
        ///   The protection to apply to the allocated region.
        /// </param>
        /// <param name="maxSize">
        ///   The maximum size that can be allocated through the allocator.
        ///   This size may be greater than the available RAM.
        /// </param>
        /// <returns>
        ///   An allocator that can be used the reserved memory.
        /// </returns>
        /// <exception cref="IOException"/>
        public static Allocator Create(Protection protection, long maxSize)
            => Create(protection, new IntPtr(maxSize));
    }
}