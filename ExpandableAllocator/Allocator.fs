namespace ExpandableAllocator

#nowarn "25" "51"

open Foreign
open System
open System.IO

/// Represents the authorization of an allocated memory block.
[<Flags>]
type Protection = Read    = 0x1
                | Write   = 0x2
                | Execute = 0x4

                | ReadWrite        = 0x3
                | ReadExecute      = 0x5
                | ReadWriteExecute = 0x7

[<AutoOpen>]
module internal ProtectionUtils =
    type Protection with
        member prot.WindowsValue =
            match prot with
            | rwx when rwx = (Protection.Read ||| Protection.Write ||| Protection.Execute) -> 0x40
            | rx  when rx  = (Protection.Read ||| Protection.Execute) -> 0x20
            | rw  when rw  = (Protection.Read ||| Protection.Write) -> 0x04

            | Protection.Read    -> 0x02
            | Protection.Execute -> 0x10

            | _ -> raise(InvalidOperationException("Cannot protect using the provided flags."))

        member prot.UnixValue = int prot


/// Defines an allocator whose memory never moves and may grow until a given amount.
type Allocator private(start: nativeint, maxSize: nativeint, protection: Protection) =
    let mutable actualSize = nativeint 0
    let mutable prot = protection

    /// Gets a pointer to the start of the allocated memory region.
    member __.Start = start

    /// Gets or sets the protection of the reserved region.
    member __.Protection
        with get() = prot
        and  set(value) = if value <> prot then
                              let os = OS.Current

                              let ok = match os with
                                       | OS.Windows ->
                                           let mutable old = 0

                                           Windows.VirtualProtect(start, actualSize, value.WindowsValue, &&old)
                                          
                                       | _ ->
                                           let mprotect = match os with
                                                          | OS.Unix -> Unix.mprotect
                                                          | OS.OSX -> OSX.mprotect

                                           mprotect(start, actualSize, value.UnixValue) = 0
                              
                              if ok then
                                  prot <- value
                              else
                                  raise(IOException("Could not change memory protection."))


    /// Gets the maximum size of memory that can be allocated.
    member __.MaximumSize = maxSize

    /// Gets or sets the actual size of the allocated memory.
    member this.ActualSize
        with get() = actualSize
        and  set(value) = if this.TryReserve(value) |> not then
                              raise(IOException("Could not change actual size of allocated memory."))

    /// Attempts to reserve the given amount of memory.
    member __.TryReserve(size: nativeint) =
        if size <= actualSize then
            true
        elif size > maxSize then
            false
        else
            let os = OS.Current

            match os with
            | OS.Windows ->
                match Windows.VirtualAlloc(start, size, 0x00001000 (* MEM_COMMIT *), prot.WindowsValue) with
                | zero when zero = IntPtr.Zero -> false
                | _ -> actualSize <- size; true

            | _ ->
                let mprotect = match os with
                               | OS.Unix -> Unix.mprotect
                               | OS.OSX -> OSX.mprotect

                match mprotect(start, size, int prot) with
                | 0 -> actualSize <- size; true
                | _ -> false
            

    interface IDisposable with
        /// Disposes the allocator, freeing all its reserved and allocated memory.
        member __.Dispose() =
            match OS.Current with
            | OS.Unix    -> Unix.munmap(start, actualSize) |> ignore
            | OS.OSX     -> OSX.munmap(start, actualSize)  |> ignore
            | OS.Windows -> Windows.VirtualFree(start, actualSize, 0x8000 (* MEM_RELEASE *)) |> ignore


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
    /// <returns>
    ///   On success, an allocator that can use the reserved memory.
    /// </returns>
    static member TryCreate(protection: Protection, maxSize: nativeint) =
        if maxSize = IntPtr.Zero then
            None
        else
            let os = Foreign.OS.Current

            match os with
            | OS.Windows ->
                match Windows.VirtualAlloc(IntPtr.Zero, maxSize, 0x00002000 (* MEM_RESERVE *), protection.WindowsValue) with
                | zero when zero = IntPtr.Zero -> None
                | addr -> Some(new Allocator(addr, maxSize, protection))

            | _ ->
                let mmap = match os with
                           | OS.Unix -> Unix.mmap
                           | OS.OSX -> OSX.mmap

                match mmap(IntPtr.Zero, maxSize, 0x0 (* PROT_NONE *), 0, 0, IntPtr.Zero) with
                | zero when zero = IntPtr.Zero -> None
                | addr -> Some(new Allocator(addr, maxSize, protection))

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
    ///   An allocator that can use the reserved memory.
    /// </returns>
    /// <exception cref="NullReferenceException" />
    static member Create(protection: Protection, maxSize: nativeint) =
        Allocator.TryCreate(protection, maxSize).Value
