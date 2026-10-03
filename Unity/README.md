# FFmpeg.AutoGen for Unity

适用于 **Unity 6000.3.17f1 及以上**的独立 UPM 源码包。使用 `FFmpeg.AutoGen` 命名空间，保留上游 `ffmpeg` 函数、枚举、结构体、固定数组 API。Mono 与 IL2CPP 共用直接 P/Invoke；无需初始化动态绑定。

本包只含 C# 绑定，**不包含 FFmpeg 原生库**。绑定对应本仓库的 FFmpeg 9 头文件；必须配套匹配 ABI 的 FFmpeg 构建。原生库及其依赖的许可遵循各自的 LGPL/GPL 等条款。

## 以 Git submodule 接入

在 Unity 项目根目录执行：

```sh
git submodule add https://github.com/TeamMajdata/FFmpeg.AutoGen.git ThirdParty/FFmpeg.AutoGen
git submodule update --init --recursive
```

在 `Packages/manifest.json` 的 `dependencies` 内加入（保留其他依赖）：

```json
"net.majdata.ffmpeg-autogen": "file:../ThirdParty/FFmpeg.AutoGen/Unity"
```

提交父仓库的 `.gitmodules`、submodule commit 和 manifest。其他成员克隆时使用 `git clone --recurse-submodules`，已有克隆使用 `git submodule update --init --recursive`。UPM 加载的是本仓库的 **Unity 子目录**；不要把整个仓库放进 `Assets`，也不要将仓库根目录当作 UPM 包导入，否则生成器、示例和重复的 .NET 绑定可能进入编译。

如果项目自身使用 asmdef，添加对 `FFmpeg.AutoGen` 的引用，并在调用指针 API 的程序集开启 **Allow 'unsafe' Code**。无 asmdef 的脚本在 Player Settings 开启 Allow 'unsafe' Code。使用 .NET Standard 2.1 API Compatibility Level。不要同时导入原版 FFmpeg.AutoGen NuGet DLL 或另一份绑定源码，以免程序集/类型重复。

## 平台与原生插件

目标架构包括 Windows **x86/x86_64**、Linux x86_64、macOS x86_64/ARM64、Android **ARMv7/ARM64/x86_64** 和 iOS ARM64。`size_t`、`ptrdiff_t` 等按实际进程指针宽度适配，同一份源码覆盖 32/64 位，也支持 Android IL2CPP 同时构建 ARMv7 和 ARM64。WebGL 和其他平台不在此包范围内。

| 平台 | 绑定使用的库名（以 avutil 为例） | 建议部署方式 |
| --- | --- | --- |
| Windows Mono / IL2CPP / Editor | `avutil-61` | `Assets/Plugins/Windows/x86_64/avutil-61.dll` |
| Windows x86 Mono / IL2CPP | `avutil-61` | `Assets/Plugins/Windows/x86/avutil-61.dll`，仅勾选 Windows x86，关闭 Editor |
| Linux Mono / IL2CPP / Editor | `libavutil.so.61` | `Assets/Plugins/Linux/x86_64/libavutil.so.61` |
| macOS Mono / IL2CPP / Editor | `libavutil.61.dylib` | `Assets/Plugins/macOS/libavutil.61.dylib`，与 Editor/Player CPU 架构一致 |
| Android IL2CPP | `avutil` | `Assets/Plugins/Android/arm64-v8a/libavutil.so`；另行提供 x86_64 则放对应目录 |
| Android ARMv7 Mono / IL2CPP | `avutil` | `Assets/Plugins/Android/armeabi-v7a/libavutil.so`，Plugin Inspector CPU 选择 ARMv7 |
| iOS IL2CPP | `__Internal` | 将静态 `.a` 库放入 `Assets/Plugins/iOS` 并参与 Xcode 链接 |

库主版本：`avcodec=63`、`avdevice=63`、`avfilter=12`、`avformat=63`、`avutil=61`、`swresample=7`、`swscale=10`。不能将旧版本文件简单改名来匹配；结构体布局也必须一致。

在 Unity Plugin Inspector 中明确勾选插件的目标平台、CPU 和适用的 Editor OS；同名的不同平台插件不要开启 Any Platform。所有平台都必须包含 FFmpeg 的传递依赖。Linux 的 SONAME、macOS 的 install name / `@rpath` / `@loader_path`、Android 的 DT_NEEDED 必须指向实际随包部署的库名；仅重命名文件不能修复依赖。Android 插件使用无版本 `.so` 文件名。iOS 静态库需为目标设备或模拟器单独构建，并链接所需系统框架、依赖库；缺失符号应修正原生构建及 Xcode 链接设置。

Editor 始终加载当前主机平台的库，即使 Build Target 是 Windows x86 或 Android/iOS。Windows Editor 使用 x64 DLL，Win32 Player 使用 x86 DLL；需同时准备两套并分别配置 Plugin Inspector 的 Editor/CPU 平台筛选。Android 的 ARMv7 库及其全部依赖必须编译为 32 位 ARM，不能复用 ARM64 文件。构建 Windows 时在 Build Profiles 选择 Intel 32-bit；构建 Android 时在 Player Settings → Other Settings → Target Architectures 勾选 ARMv7。包通过 Unity/系统的原生插件查找规则加载，**不提供 `ffmpeg.RootPath`、`DynamicallyLoadedBindings.Initialize()` 或可变函数解析器**。

## 最小调用

```csharp
using FFmpeg.AutoGen;
using UnityEngine;

public class PrintFFmpegVersion : MonoBehaviour
{
    void Start() => Debug.Log(ffmpeg.av_version_info());
}
```

Package Manager → FFmpeg.AutoGen for Unity → Samples → **Native smoke test** 可导入完整示例。将 `NativeSmokeTest` 挂到场景对象，在桌面平台和 Android ARMv7 分别构建 Mono / IL2CPP Player；检查版本、UTF-8 字典、值类型参数、固定数组和原生回调。Android ARM64/x86_64 和 iOS 使用 IL2CPP。

## 字符串、回调和内存

普通函数的 `string` 参数在调用期间显式转为 UTF-8；返回的 `const char*` 被复制成字符串，原生指针不会被自动释放。数组通过 `fixed` 固定，native 入口只接收原生指针。原生函数保留某个指针供调用后使用时，应使用指针 API 并自行管理生命周期。

含字符串的函数另提供 `_utf8` 形式，例如 `ffmpeg.av_version_info_utf8()` 和 `ffmpeg.av_dict_set_utf8(...)`。它们直接接受/返回 `byte*`，遵循 FFmpeg 的原始内存所有权约定。`av_dict_set` 的 string 形式拒绝 `AV_DICT_DONT_STRDUP_KEY/VAL`，`av_dict_set_int` 的 string 形式拒绝 `AV_DICT_DONT_STRDUP_KEY`；转移内存所有权必须使用 `_utf8` 形式并传入由 FFmpeg 分配的指针。

传给 native 的回调必须是静态方法，加上 `[AOT.MonoPInvokeCallback(typeof(具体委托类型))]`，并在整个原生使用期间持有委托强引用。不要传捕获闭包或实例方法，也不要让异常越过 native 边界。回调可能来自后台线程；Unity API 调用应切回主线程。包不通过全量 `link.xml` 保留所有 FFmpeg API，以免 iOS 链接未使用或平台专用的符号；自行通过反射调用的方法需要在项目中额外保留。

与上游的差异：三个回调 `av_log_set_callback_callback`、`AVClass_query_ranges`、`AVFormatContext_io_open` 的字符串参数是 `byte*`，可用 `ffmpeg.PtrToStringUTF8(pointer)` 读取；嵌套的函数指针参数使用 `IntPtr`。日志回调中的 `va_list` 仍是平台原生对象，不能当作托管参数数组解读，可在回调期间传给 `av_log_format_line2_utf8`。`AVIOContext.checksum` 和校验和回调的 C `unsigned long` 在 Windows 为 `uint`，在 Unix 平台为 `UIntPtr`（ARMv7 为 32 位，ARM64/x64 为 64 位）。Windows SDK 的 GUID/DXVA 字段始终为 32 位。

## 从早期 64 位 Unity 包迁移

原生 `size_t` 参数、返回值及字段现在使用 C# 9 的 `nuint`（`UIntPtr`）；`ptrdiff_t` 和 `intptr_t` 使用 `nint`（`IntPtr`）。这是为了正确支持 32 位所需的源代码 API 调整，64 位调用方也应更新。常量如 `ffmpeg.av_malloc(1024)` 可以直接传入；现有 `ulong` 长度变量请用 `checked((nuint)length)` 转换，避免在 32 位进程截断。

指针输出参数也要改，例如 `av_packet_get_side_data(..., nuint* size)` 应使用 `nuint size = 0` 后传 `&size`，不能继续传 `ulong*`。`av_buffer_pool_init_alloc` 等回调的长度参数同步改为 `nuint`。图像 API 中 `size_t[4]` / `ptrdiff_t[4]` 分别使用新增的 `nuint_array4` / `nint_array4`，例如 `av_image_fill_plane_sizes`、`av_image_copy_uc_from`。原有 `long_array4` 仍用于 `int64_t` 时间戳，不应全局替换。

`AVFrame.pts`、`AVPacket.pts/dts/duration`、seek offset 等 `int64_t` 数据在 32 位平台也仍然是 `long`。类型转换按头文件的具体声明生成，不依赖 `UNITY_64`，因此不会把 Android 多架构包的一个架构布局误用到另一个架构。

本次 Unity 6000.3.17f1 x64 IL2CPP 检查还发现：`(nuint)0xf1234567U` 在生成的 C++ 中会错误地先转成有符号 `int32_t` 再扩展。需要将最高位为 1 的 `uint` 常量传为原生无符号整数时，可使用 `new UIntPtr(0xf1234567U)`；测试用此形式保留完整高位值。绑定的 P/Invoke 返回类型本身已确认生成为正确的 `uintptr_t`。

FFmpeg 本身的可用 codec、硬件加速、设备、协议和函数由原生构建决定；该包不会替你提供未编译进 native 的功能。

## 更新绑定与验证

维护者更新上游头文件/绑定后，先用 Clang 从头文件重新提取原生类型（Unity Android NDK 自带的 Clang 即可）：

```sh
python Tools/extract-unity-native-types.py --clang "<NDK>/toolchains/llvm/prebuilt/windows-x86_64/bin/clang.exe"
```

随后在仓库根目录运行：

```sh
python Tools/generate-unity.py
python Tools/generate-unity.py --check
```

`Tools/unity-native-types.json` 记录 Clang AST 提取的成员级 `size_t` / `ptrdiff_t` / `intptr_t` / `uintptr_t` 信息及输入文件摘要；生成时校验所有映射恰好应用一次，头文件或上游绑定变化后会拒绝使用过期数据。提取脚本的 ARMv7 和 ARM64 结果应一致，可用 `--target aarch64-linux-android23 --check` 检查。

生成脚本从已检入的绑定和类型元数据派生本包，保留文档和仍表示固定宽度整数的旧版数组类型，转换为 C# 9，并生成稳定 `.meta` GUID。仅重新生成本包无需 Clang；包使用者不需要安装 Python 或运行任何生成工具。

`Tests/UnitySmoke~` 为 C# 9/.NET Standard 2.1 的编译矩阵与原生 ABI 测试；`Tests/UnityPlayer~` 为隔离的真实 Unity Player 测试。测试 stub 只检验托管互操作契约，不是 FFmpeg 实现。发布前仍应使用实际 FFmpeg 构建在目标 OS/设备运行导入的 Native smoke test，并验证真实媒体解码。

本次验证（2026-10-03，Unity **6000.3.17f1**，Player 开启 High managed stripping）：

| 检查 | 结果 |
| --- | --- |
| C# 9/.NET Standard 2.1 平台及架构符号矩阵 | 9 组编译和互操作元数据检查通过 |
| Windows x86/x64 原生 ABI 回归 | 真实 32 位和 64 位 .NET 进程各通过 9026 项断言；布局探针直接编译仓库 FFmpeg 头文件 |
| Windows x86 Mono Player | 构建、运行通过，进程指针宽度为 4 |
| Windows x86 IL2CPP Player（Debug C++ 配置） | 构建、运行通过，进程指针宽度为 4 |
| Windows x86 IL2CPP Player（Release C++ 配置） | 构建成功，但本机运行时在引擎启动阶段崩溃；空项目亦复现 |
| Windows x64 Mono / IL2CPP Release Player | 构建、运行回归通过，进程指针宽度为 8 |
| Android ARMv7 Mono / IL2CPP | APK 构建通过；随包插件确认是 `armeabi-v7a` ELF32 ARM，尚未运行设备测试 |

本机的 Win32 IL2CPP Release 崩溃发生在 `Runtime::Init` / `GC_grow_table`，未进入测试或绑定调用。移除 FFmpeg 包及所有原生插件的空项目仍在相同位置崩溃，因此不能将 Release 运行验收标为通过。此环境使用 Unity 6000.3.17f1 与 MSVC 14.51；目前通过验证的 Win32 IL2CPP 配置是 **C++ Compiler Configuration = Debug**，这与托管脚本的 Debug/Release 编译无关。测试 runner 使用 `-Architecture x86 -Backend IL2CPP -CompilerConfiguration Debug` 选择该配置，并单独保留 Release 的失败记录。

尚未在 Linux、macOS、Android、iOS 运行 Player，也未验证实际 FFmpeg 的媒体解码。目标环境仍需部署匹配位数/ABI 的 FFmpeg 库并进行真实媒体验收。

参考：[Unity 原生插件](https://docs.unity3d.com/6000.3/Documentation/Manual/plug-ins-native.html)、[后端支持平台](https://docs.unity3d.com/6000.3/Documentation/Manual/scripting-backends-intro.html)、[AOT/回调限制](https://docs.unity3d.com/6000.3/Documentation/Manual/ScriptingRestrictions.html)、[本地 UPM 路径](https://docs.unity3d.com/6000.3/Documentation/Manual/upm-localpath.html)。
