# Unity binding regression harness

Run from the repository root with a .NET 9 SDK/runtime and a native C compiler:

```powershell
pwsh -File 'Tests/UnitySmoke~/run.ps1'
```

The harness uses no NuGet packages and its local NuGet.Config disables remote feeds.
The trailing `~` keeps the harness out of Unity's asset import. Build outputs stay
inside the ignored `bin` and `obj` directories.

`-CompileOnly` skips the native runtime check; `-SkipMatrix` skips the seven
platform-symbol configurations; `-Compiler <path>` selects GCC or Clang.
On Windows the runner also recognizes Strawberry Perl's bundled GCC.

The binding project compiles the actual `Unity/Runtime/**/*.cs` sources using C# 9
against .NET Standard 2.1. Each Windows, Linux, macOS, Android and iOS configuration
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

The C library implements only enough exports to exercise interop and must never
be shipped as FFmpeg. `NativeLibrary.SetDllImportResolver` is used by the .NET test
host only; it is not part of the Unity package. The C struct mirrors the checked-in
header layout and is not an independent validation of a downloaded FFmpeg build.

These checks do not run Mono or IL2CPP and do not cross-compile native binaries.
Platform-symbol compilation establishes managed API compatibility, not runtime
support on a device. Actual Unity player builds and matching native FFmpeg binaries
remain necessary for each deployed operating system and architecture.
