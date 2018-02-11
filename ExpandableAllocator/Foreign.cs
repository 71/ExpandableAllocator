using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace ExpandableAllocator
{
    internal enum OS
    {
        Unix, Windows, OSX
    }

    internal static class Foreign
    {
        public static OS CurrentOS
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                    return OS.Windows;
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                    return OS.Unix;
                if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                    return OS.OSX;

                throw new PlatformNotSupportedException();
            }
        }

        public static class Unix
        {
            private const string LIB = "libc.so.6";

            [DllImport(LIB, ExactSpelling = true)]
            public static extern IntPtr mmap(IntPtr addr, IntPtr length, int prot, int flags, int fd, IntPtr offset);

            [DllImport(LIB, ExactSpelling = true)]
            public static extern int munmap(IntPtr addr, IntPtr length);

            [DllImport(LIB, ExactSpelling = true)]
            public static extern int mprotect(IntPtr addr, IntPtr length, int prot);
        }

        public static class OSX
        {
            private const string LIB = "libSystem.dylib";

            [DllImport(LIB, ExactSpelling = true)]
            public static extern IntPtr mmap(IntPtr addr, IntPtr length, int prot, int flags, int fd, IntPtr offset);

            [DllImport(LIB, ExactSpelling = true)]
            public static extern int munmap(IntPtr addr, IntPtr length);

            [DllImport(LIB, ExactSpelling = true)]
            public static extern int mprotect(IntPtr addr, IntPtr length, int prot);
        }

        public static class Windows
        {
            private const string LIB = "kernel32.dll";

            [DllImport(LIB, ExactSpelling = true)]
            public static extern IntPtr VirtualAlloc(IntPtr addr, IntPtr dwSize, int flAllocationType, int flProtect);

            [DllImport(LIB, ExactSpelling = true)]
            public static extern bool VirtualFree(IntPtr addr, IntPtr dwSize, int dwFreeType);

            [DllImport(LIB, ExactSpelling = true)]
            public static extern bool VirtualProtect(IntPtr addr, IntPtr dwSize, int flNewProtect, out int flProtect);
        }
    }
}
