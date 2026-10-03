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
"com.teammajdata.ffmpeg-autogen": "file:../ThirdParty/FFmpeg.AutoGen/Unity"
```

提交父仓库的 `.gitmodules`、submodule commit 和 manifest。其他成员克隆时使用 `git clone --recurse-submodules`，已有克隆使用 `git submodule update --init --recursive`。UPM 加载的是本仓库的 **Unity 子目录**；不要把整个仓库放进 `Assets`，也不要将仓库根目录当作 UPM 包导入，否则生成器、示例和重复的 .NET 绑定可能进入编译。

如果项目自身使用 asmdef，添加对 `FFmpeg.AutoGen` 的引用，并在调用指针 API 的程序集开启 **Allow 'unsafe' Code**。无 asmdef 的脚本在 Player Settings 开启 Allow 'unsafe' Code。使用 .NET Standard 2.1 API Compatibility Level。不要同时导入原版 FFmpeg.AutoGen NuGet DLL 或另一份绑定源码，以免程序集/类型重复。

## 平台与原生插件

当前生成结果使用 64 位 `size_t` / `ptrdiff_t`。Windows/Linux x86_64、macOS x86_64/ARM64、Android ARM64/x86_64、iOS ARM64 是目标架构；32 位进程首次调用会明确报错。WebGL 和其他平台不在此包范围内。

| 平台 | 绑定使用的库名（以 avutil 为例） | 建议部署方式 |
| --- | --- | --- |
| Windows Mono / IL2CPP / Editor | `avutil-61` | `Assets/Plugins/Windows/x86_64/avutil-61.dll` |
| Linux Mono / IL2CPP / Editor | `libavutil.so.61` | `Assets/Plugins/Linux/x86_64/libavutil.so.61` |
| macOS Mono / IL2CPP / Editor | `libavutil.61.dylib` | `Assets/Plugins/macOS/libavutil.61.dylib`，与 Editor/Player CPU 架构一致 |
| Android IL2CPP | `avutil` | `Assets/Plugins/Android/arm64-v8a/libavutil.so`；另行提供 x86_64 则放对应目录 |
| iOS IL2CPP | `__Internal` | 将静态 `.a` 库放入 `Assets/Plugins/iOS` 并参与 Xcode 链接 |

库主版本：`avcodec=63`、`avdevice=63`、`avfilter=12`、`avformat=63`、`avutil=61`、`swresample=7`、`swscale=10`。不能将旧版本文件简单改名来匹配；结构体布局也必须一致。

在 Unity Plugin Inspector 中明确勾选插件的目标平台、CPU 和适用的 Editor OS；同名的不同平台插件不要开启 Any Platform。所有平台都必须包含 FFmpeg 的传递依赖。Linux 的 SONAME、macOS 的 install name / `@rpath` / `@loader_path`、Android 的 DT_NEEDED 必须指向实际随包部署的库名；仅重命名文件不能修复依赖。Android 插件使用无版本 `.so` 文件名。iOS 静态库需为目标设备或模拟器单独构建，并链接所需系统框架、依赖库；缺失符号应修正原生构建及 Xcode 链接设置。

Editor 始终加载当前主机平台的库，即使 Build Target 是 Android/iOS。因此开发移动端时也需安装一份匹配 Editor 的桌面库。包通过 Unity/系统的原生插件查找规则加载，**不提供 `ffmpeg.RootPath`、`DynamicallyLoadedBindings.Initialize()` 或可变函数解析器**。

## 最小调用

```csharp
using FFmpeg.AutoGen;
using UnityEngine;

public class PrintFFmpegVersion : MonoBehaviour
{
    void Start() => Debug.Log(ffmpeg.av_version_info());
}
```

Package Manager → FFmpeg.AutoGen for Unity → Samples → **Native smoke test** 可导入完整示例。将 `NativeSmokeTest` 挂到场景对象，在桌面平台分别构建 Mono / IL2CPP Player；检查版本、UTF-8 字典、值类型参数、固定数组和原生回调。Android ARM64/x86_64 和 iOS 使用 IL2CPP。Unity 6.3 的 Android Mono 仅支持 ARMv7，与本包的 64 位要求不符。

## 字符串、回调和内存

普通函数的 `string` 参数在调用期间显式转为 UTF-8；返回的 `const char*` 被复制成字符串，原生指针不会被自动释放。数组通过 `fixed` 固定，native 入口只接收原生指针。原生函数保留某个指针供调用后使用时，应使用指针 API 并自行管理生命周期。

含字符串的函数另提供 `_utf8` 形式，例如 `ffmpeg.av_version_info_utf8()` 和 `ffmpeg.av_dict_set_utf8(...)`。它们直接接受/返回 `byte*`，遵循 FFmpeg 的原始内存所有权约定。`av_dict_set` 的 string 形式拒绝 `AV_DICT_DONT_STRDUP_KEY/VAL`，`av_dict_set_int` 的 string 形式拒绝 `AV_DICT_DONT_STRDUP_KEY`；转移内存所有权必须使用 `_utf8` 形式并传入由 FFmpeg 分配的指针。

传给 native 的回调必须是静态方法，加上 `[AOT.MonoPInvokeCallback(typeof(具体委托类型))]`，并在整个原生使用期间持有委托强引用。不要传捕获闭包或实例方法，也不要让异常越过 native 边界。回调可能来自后台线程；Unity API 调用应切回主线程。包不通过全量 `link.xml` 保留所有 FFmpeg API，以免 iOS 链接未使用或平台专用的符号；自行通过反射调用的方法需要在项目中额外保留。

与上游的差异：三个回调 `av_log_set_callback_callback`、`AVClass_query_ranges`、`AVFormatContext_io_open` 的字符串参数是 `byte*`，可用 `ffmpeg.PtrToStringUTF8(pointer)` 读取；嵌套的函数指针参数使用 `IntPtr`。日志回调中的 `va_list` 仍是平台原生对象，不能当作托管参数数组解读，可在回调期间传给 `av_log_format_line2_utf8`。`AVIOContext.checksum` 和校验和回调的 C `unsigned long` 在 Windows 为 `uint`，在这些 64 位 Unix 平台为 `ulong`。Windows SDK 的 GUID/DXVA 字段始终为 32 位。

FFmpeg 本身的可用 codec、硬件加速、设备、协议和函数由原生构建决定；该包不会替你提供未编译进 native 的功能。

## 更新绑定与验证

维护者更新上游头文件/绑定后，在仓库根目录运行：

```sh
python Tools/generate-unity.py
python Tools/generate-unity.py --check
```

脚本从已检入的生成结果派生本包，保留文档和旧版固定数组类型名，转换为 C# 9，并生成稳定 `.meta` GUID。包使用者不需要安装 Python 或运行生成器。

`Tests/UnitySmoke~` 为 C# 9/.NET Standard 2.1 的编译矩阵与原生 ABI 测试；`Tests/UnityPlayer~` 为隔离的真实 Unity Player 测试。测试 stub 只检验托管互操作契约，不是 FFmpeg 实现。发布前仍应使用实际 FFmpeg 构建在目标 OS/设备运行导入的 Native smoke test，并验证真实媒体解码。

本次验证（2026-10-03）：Unity **6000.3.17f1** Windows x64 Editor、Mono Player、IL2CPP Player 构建/运行通过，Player 开启 High managed stripping；五个平台以及 Windows Editor 切换 Android/iOS 的七组 C# 9/.NET Standard 2.1 编译和互操作元数据检查通过。尚未在 Linux、macOS、Android、iOS 运行 Player，也未验证实际 FFmpeg 的媒体解码。上述平台配置已实现，运行验收仍需对应设备及匹配的原生库。

参考：[Unity 原生插件](https://docs.unity3d.com/6000.3/Documentation/Manual/plug-ins-native.html)、[后端支持平台](https://docs.unity3d.com/6000.3/Documentation/Manual/scripting-backends-intro.html)、[AOT/回调限制](https://docs.unity3d.com/6000.3/Documentation/Manual/ScriptingRestrictions.html)、[本地 UPM 路径](https://docs.unity3d.com/6000.3/Documentation/Manual/upm-localpath.html)。
