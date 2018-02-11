module Tests

#nowarn "9"

open ExpandableAllocator

open FsUnitTyped
open Microsoft.FSharp.NativeInterop
open NUnit.Framework

open System.Runtime.ExceptionServices
open System.Security


let allocator(size: nativeint) = Allocator.Create(Protection.Read ||| Protection.Write, size)
let kb = nativeint 1024
let zero = nativeint 0


[<TestCase(1_000L); TestCase(1_000_000L); TestCase(1_000_000_000L); TestCase(1_000_000_000_000L)>]
let ``should be able to reserve large chunks of memory`` (size: int64) =
    let size = nativeint size
    let success, alloc = Allocator.TryCreate(Protection.Read, size)
    
    success |> shouldEqual true

    use alloc = alloc

    alloc.MaximumSize |> shouldEqual size
    alloc.ActualSize |> shouldEqual zero

[<TestCase(Protection.Read); TestCase(Protection.ReadWrite); TestCase(Protection.ReadWriteExecute)>]
let ``should have valid protection``(protection: Protection) =
    use alloc = Allocator.Create(protection, kb)

    alloc.Protection |> shouldEqual protection

    if protection.HasFlag(Protection.Write) then

        // test write protection
        alloc.TryReserve(kb) |> shouldEqual true

        let ptr = NativePtr.ofNativeInt(alloc.Address)

        NativePtr.write ptr 42
        NativePtr.read ptr |> shouldEqual 42

[<Test>]
[<HandleProcessCorruptedStateExceptions; SecurityCritical>]
let ``should be able to change protection``() =
    use alloc = Allocator.Create(Protection.Read, kb)
    let ptr = NativePtr.ofNativeInt(alloc.Address)

    alloc.ActualSize <- nativeint 512

    // Some of the following code currently does not work in .NET Core (2018-02-11)
    // Skipping tests whose failures cannot be caught

    // try
    //     NativePtr.write ptr 42
    //     failwith "The protection shouldn't have allowed this."
    // with
    // | :? AccessViolationException -> ()
    // | _ -> reraise()

    alloc.Protection <- Protection.ReadWrite

    NativePtr.write ptr 42
    NativePtr.read ptr |> shouldEqual 42

    // alloc.Protection <- Protection.Read

    // shouldFail <| fun () ->
    //     NativePtr.write ptr 24

    // NativePtr.read ptr |> shouldNotEqual 24

[<Test>]
let ``should not move memory on realloc`` () =
    let alloc = allocator(kb)
    let addr = alloc.Address

    alloc.ActualSize |> shouldEqual (nativeint 0)

    alloc.TryReserve(nativeint 512) |> shouldEqual true
    alloc.ActualSize |> shouldEqual (nativeint 512)
    alloc.Address |> shouldEqual addr

    alloc.TryReserve(nativeint 1024) |> shouldEqual true
    alloc.ActualSize |> shouldEqual (nativeint 1024)
    alloc.Address |> shouldEqual addr

[<Test>]
let ``should be able to allocate memory until maximum is reached``() =
    do
        use alloc = allocator(nativeint 1024)

        alloc.TryReserve(nativeint 1024) |> shouldEqual true
        alloc.ActualSize |> shouldEqual (nativeint 1024)
    
    do
        use alloc = allocator(nativeint 1024)

        alloc.TryReserve(nativeint 2048) |> shouldEqual false
        alloc.ActualSize |> shouldEqual (nativeint 0)
