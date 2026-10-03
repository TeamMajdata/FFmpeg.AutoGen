# Real Unity player smoke tests

These tests import the local `Unity/` UPM package into isolated projects and exercise its native interop in Unity 6000.3.17f1. Windows x86/x64 Mono and IL2CPP players are built and run. Android ARMv7 Mono/IL2CPP and ARM64 IL2CPP APKs can be built; APK construction does not establish device runtime compatibility. The C stubs come from `../UnitySmoke~/native/smoke.c`, with ABI probes compiled directly against the checked-in FFmpeg headers through `native/abi.c`. These are not FFmpeg binaries and do not decode media.

Run from the repository root in PowerShell:

```powershell
# Windows 32-bit builds, including a separate 64-bit Editor smoke.
& 'Tests/UnityPlayer~/run.ps1' -Architecture x86 -Backend Mono
& 'Tests/UnityPlayer~/run.ps1' -Architecture x86 -Backend IL2CPP

# Windows 64-bit regression and Editor-only checks.
& 'Tests/UnityPlayer~/run.ps1' -Architecture x64 -Backend Mono
& 'Tests/UnityPlayer~/run.ps1' -Architecture x64 -Backend IL2CPP
& 'Tests/UnityPlayer~/run.ps1' -Backend Editor

# Android APK builds. Mono supports ARMv7; ARM64 requires IL2CPP.
& 'Tests/UnityPlayer~/run.ps1' -Platform Android -Architecture armv7 -Backend Mono
& 'Tests/UnityPlayer~/run.ps1' -Platform Android -Architecture armv7 -Backend IL2CPP
& 'Tests/UnityPlayer~/run.ps1' -Platform Android -Architecture arm64 -Backend IL2CPP
```

The default editor is `C:\Program Files\Unity Editors\6000.3.17f1\Editor\Unity.exe`; override it with `-UnityEditor`. Native test libraries are built automatically with MSVC for Windows, and the Unity installation's Android NDK for Android. Install MSVC x86/x64 C++ tools, a Windows SDK, the required Unity platform support modules, and the bundled Android SDK/NDK/OpenJDK. Unity needs its normal licensing, Package Manager IPC, and cache access. Gradle might need network access to populate its dependency cache on the first Android build.

IL2CPP uses the Release C++ compiler configuration by default. Pass `-CompilerConfiguration Debug` or `Master` to exercise another configuration; these runs use separate `IL2CPP-Debug/` or `IL2CPP-Master/` result directories and do not replace the Release reports. The runner reports failures directly and does not silently retry with another configuration.

To reuse compiled stubs, pass `-SkipNativeBuild`. To supply a different native stub root, also pass `-NativeDirectory`; that directory must contain `Windows-x64/avutil-61.dll` and `swscale-10.dll`, plus the matching `Windows-x86/` or `Android-armv7/` / `Android-arm64/` libraries for the selected target. `build-native.ps1` can be run separately. Native stubs are statically linked to the MSVC runtime, so players do not need a test-specific runtime installation.

The runner creates separate `Projects/<platform>-<architecture>/` projects, with reports and logs in `Results/<platform>-<architecture>/<backend>/`. The projects explicitly configure plugin importers: Windows x64 stubs serve the Editor, Windows x86 stubs serve only the 32-bit player, and Android stubs serve only their selected ABI. Build targets use their own plugins even though Editor checks always execute as 64-bit Windows. No existing user Unity project is opened or modified. Run backends for the same target sequentially because they share a generated project. `Projects/`, `Native/`, `Results/`, and the older `Project/` are ignored.

Checks cover native struct size/offsets, pointer-sized `size_t` arguments/returns/output pointers and callbacks, signed `ptrdiff_t` strides and arrays, genuine 64-bit timestamps on 32-bit players, UTF-8 and null strings, `AVRational` by value, pinned arrays, and AOT reverse P/Invoke callbacks. Player builds use high managed stripping and keep callback delegates rooted during forced GC. Every Windows player reports its actual pointer size.

For device testing, install an APK on a compatible device and launch `com.teammajdata.ffmpegsmoke`. The application runs the smoke checks at startup, writes `smoke-report.txt` under `Application.persistentDataPath`, logs the result, and exits. Read its Unity log with `adb logcat -s Unity`. Installing or launching on devices is separate from this runner. A build report only certifies APK construction; a device log beginning `PASS:` certifies that device's interop checks.

Passing these checks does not validate real FFmpeg decoding, macOS/Linux deployment, iOS linking/device execution, codecs, or hardware acceleration. Those require matching FFmpeg builds and target-platform runtime tests.
