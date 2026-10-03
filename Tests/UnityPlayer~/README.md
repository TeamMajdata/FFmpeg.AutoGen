# Real Unity player smoke tests

These tests import the local `Unity/` UPM package into an isolated project and run the same native interop checks in the Unity Editor, a Windows x64 Mono player, and a Windows x64 IL2CPP player. They use the small C test double in `../UnitySmoke~/native/smoke.c`; they do not test FFmpeg decoding or ship FFmpeg binaries.

Build the native stubs with `Tests/UnitySmoke~/run.ps1`, then run from the repository root:

```powershell
& 'Tests/UnityPlayer~/run.ps1' -Backend Editor
& 'Tests/UnityPlayer~/run.ps1' -Backend Mono
& 'Tests/UnityPlayer~/run.ps1' -Backend IL2CPP
```

The default editor is `C:\Program Files\Unity Editors\6000.3.17f1\Editor\Unity.exe`. Override it with `-UnityEditor`. The Windows IL2CPP build support module and a Unity-compatible C++ toolchain must be installed. Unity also needs access to its normal licensing, Package Manager IPC, and cache directories.

Checks cover native `AVIOContext` size/offsets, fixed array layout and by-reference arguments, UTF-8 input and return values, null strings, by-value `AVRational`, pinned managed arrays, and static reverse P/Invoke callbacks with `[MonoPInvokeCallback]`. The player build enables high managed stripping. Callback delegate instances remain rooted during a forced GC.

The generated `Project/` and `Results/` directories are ignored. Editor/build logs, player logs, and explicit pass reports live in `Results/<backend>/`. No existing Unity project is opened or modified. Run the two player builds sequentially because they share the isolated project.

Passing these Windows tests does not validate Android/iOS device execution, macOS/Linux deployment, actual FFmpeg binaries, codecs, or platform-specific hardware acceleration. Those require matching native libraries and target-platform builds.
