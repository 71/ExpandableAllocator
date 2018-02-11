module internal Foreign

#nowarn "25" "1182"

open System.Runtime.InteropServices

type PROT = EXEC  = 3
          | READ  = 1
          | WRITE = 2
          | NONE  = 0

[<RequireQualifiedAccess>]
type OS = Unix | OSX | Windows
with
    static member Current =
        if RuntimeInformation.IsOSPlatform(OSPlatform.Windows) then
            Windows
        elif RuntimeInformation.IsOSPlatform(OSPlatform.OSX) then
            OSX
        else
            Unix

module Unix =
    [<Literal>]
    let LIB = "libc.so.6"

    [<DllImport(LIB, ExactSpelling = true)>]
    extern void* mmap(void* addr, nativeint length, int prot, int flags, int fd, nativeint offset)

    [<DllImport(LIB, ExactSpelling = true)>]
    extern int munmap(void* addr, nativeint length)

    [<DllImport(LIB, ExactSpelling = true)>]
    extern int mprotect(void* addr, nativeint length, int prot)


module OSX =
    [<Literal>]
    let LIB = "libSystem.dylib"

    [<DllImport(LIB, ExactSpelling = true)>]
    extern void* mmap(void* addr, nativeint length, int prot, int flags, int fd, nativeint offset)

    [<DllImport(LIB, ExactSpelling = true)>]
    extern int munmap(void* addr, nativeint length)

    [<DllImport(LIB, ExactSpelling = true)>]
    extern int mprotect(void* addr, nativeint length, int prot)


module Windows =
    [<Literal>]
    let LIB = "kernel32.dll"

    [<DllImport(LIB, ExactSpelling = true)>]
    extern void* VirtualAlloc(void* lpAddress, nativeint dwSize, int flAllocationType, int flProtect)

    [<DllImport(LIB, ExactSpelling = true)>]
    extern bool VirtualFree(void* lpAddress, nativeint dwSize, int dwFreeType)

    [<DllImport(LIB, ExactSpelling = true)>]
    extern bool VirtualProtect(void* lpAddress, nativeint dwSize, int flNewProtect, int* lpflOldProtect)
