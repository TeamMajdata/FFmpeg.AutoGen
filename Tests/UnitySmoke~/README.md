# Unity binding regression harness

Run from the repository root with a .NET 9 SDK/runtime and a native C compiler:

```powershell
pwsh -File 'Tests/UnitySmoke~/run.ps1'
# Windows x64 plus a real Windows x86 process (MSVC x86 tools and x86 .NET 9 runtime):
pwsh -File 'Tests/UnitySmoke~/run.ps1' -WindowsX86
```

The harness uses no NuGet packages and its local NuGet.Config disables remote feeds.
The trailing `~` keeps the harness out of Unity's asset import. Build outputs stay
inside the ignored `bin` and `obj` directories.

`-CompileOnly` skips the native runtime check; `-SkipMatrix` skips the nine
platform-symbol configurations; `-Compiler <path>` selects GCC or Clang.
On Windows the runner also recognizes Strawberry Perl's bundled GCC.

The binding project compiles the actual `Unity/Runtime/**/*.cs` sources using C# 9
against .NET Standard 2.1. Each Windows x64/x86, Linux, macOS, Android ARM64/ARMv7 and iOS configuration
is checked for platform library names, C `unsigned long` width, errno values,
explicit Cdecl signatures, and absence of runtime string/array marshalling.
The two Windows Editor configurations additionally select Android and iOS targets
to verify that the host Editor libraries and ABI take precedence.

The .NET console host loads a deliberately small native C test double. It checks:

- Repeated borrowed UTF-8 returns, Unicode string input, and null versus empty strings.
- Dictionary UTF-8 round trips, error returns, deletion, managed ownership rejection,
  raw native ownership transfer, and balanced test-library allocations.
- Rooted static callbacks invoked by native code after a managed collection.
- Array pinning and native writes, null arrays, fixed-array structs passed by `in`
  and `ref`, and `AVRational` passed and returned by value.
- `AVIOContext` size and affected field offsets against the host C compiler's ABI.
- Pointer-sized unsigned arguments and returns with the high bit set, a `size_t*`
  output with adjacent sentinels, a native buffer pool callback taking `size_t`,
  pointer-sized struct fields, negative `ptrdiff_t` strides and arrays, and 64-bit
  timestamps that must retain their upper bits in a 32-bit process.
- `AVBufferRef`, `AVPacket`, `AVFrame`, their side-data structs, `AVCodecContext`
  and `AVFormatContext` sizes and offsets, including mixed native-width pointers,
  size fields, callback pointers and fixed-width 64-bit integer fields.

The C library implements only enough exports to exercise interop and must never
be shipped as FFmpeg. `NativeLibrary.SetDllImportResolver` is used by the .NET test
host only; it is not part of the Unity package. ABI probes compile directly against
the repository's checked-in FFmpeg headers. They do not validate the ABI of a
separately downloaded FFmpeg build.

The optional Windows x86 check uses MSVC to compile a real 32-bit DLL and runs the
same assertions under the x86 .NET runtime. Its output reports the process bitness;
native and managed pointer widths must match. The default Windows check remains x64.

These checks do not run Mono or IL2CPP. Android ARMv7 receives managed compilation
and metadata validation here; native Android execution belongs in the Unity player tests.
Platform-symbol compilation establishes managed API compatibility, not runtime
support on a device. Actual Unity player builds and matching native FFmpeg binaries
remain necessary for each deployed operating system and architecture.
