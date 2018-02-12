ExpandableAllocator
===================
A low-level .NET allocator that grows lazily.

## Usage
```fsharp
// Reserve 1 TB of memory.
use alloc = Allocator.Create(Protection.Read, 0x1_000_000_000_000L)

let addr = alloc.Address
let ptr = NativePtr.fromNativeInt addr

// Actually allocate 512 bytes.
alloc.ActualSize <- nativeint 512

// Make sure the pointer hasn't moved.
alloc.Address |> should equal addr

// Read something.
NativePtr.read ptr |> should equal 0

// This would throw an AccessViolationException:
// NativePtr.write ptr 42

// Change the region's protection.
alloc.Protection <- Protection.ReadWrite

// Now, actually write our value.
NativePtr.write ptr 42
NativePtr.read ptr |> should equal 42

// A safe wrapper is also provided.
use stream = new ExpandableStream(alloc)

stream.CanWrite |> should equal true
stream.CanRead  |> should equal true
stream.CanSeek  |> should equal true
```

More examples can be seen in the [Tests](./ExpandableAllocator.Tests) directory.

## Installation
[![NuGet](https://img.shields.io/nuget/vpre/ExpandableAllocator.svg)](https://nuget.org/packages/ExpandableAllocator)
```powershell
Install-Package ExpandableAllocator
```

## How does it work?
Internally, the `Allocator` **reserves** memory when it is created
(it does not actually allocate it) using [`VirtualAlloc`][va]
or [`mmap`][mmap].  
This means that large chunks of memory (ie greater than 1TB) can be reserved
without any allocation.

Then, when more memory is actually needed (using `TryReserve` or `ActualSize`),
the `Allocator` commits the memory using [`VirtualAlloc`][va] or [`mprotect`][mprotect].

Finally, when the `Allocator` is `Dispose`d, [`VirtualFree`][vf] or [`munmap`][munmap]
is called on the allocated memory region.


[va]: https://msdn.microsoft.com/library/windows/desktop/aa366887
[vf]: https://msdn.microsoft.com/library/windows/desktop/aa366892
[mmap]: http://man7.org/linux/man-pages/man2/mmap.2.html
[mprotect]: http://man7.org/linux/man-pages/man2/mprotect.2.html
[munmap]: http://man7.org/linux/man-pages/man2/munmap.2.html
