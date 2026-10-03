using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using FFmpeg.AutoGen;

internal static unsafe class Program
{
    private const string UnicodeText = "中文 / café / 日本語 / 😀";
    private static readonly av_buffer_create_free BufferCallback = ReleaseBuffer;
    private static readonly av_log_set_callback_callback LogCallback = ReceiveLog;
    private static int _assertions;
    private static int _bufferCalls;
    private static byte _bufferByte;
    private static IntPtr _bufferOpaque;
    private static string _logText;
    private static int _logLevel;
    private static IntPtr _logOpaque;
    private static IntPtr _native;

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int NativeInt();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void NativeVoid();

    private static int Main(string[] args)
    {
        try
        {
            if (args.Length == 2 && args[0] == "--metadata")
            {
                CheckMetadata(args[1]);
                Console.WriteLine("PASS " + args[1] + " C# 9 / .NET Standard 2.1 metadata: " + _assertions + " assertions.");
                return 0;
            }

            if (args.Length != 1)
                throw new ArgumentException("Pass a native test library path, or --metadata <platform>.");

            _native = NativeLibrary.Load(Path.GetFullPath(args[0]));
            NativeLibrary.SetDllImportResolver(typeof(ffmpeg).Assembly, (name, assembly, path) => _native);
            CheckMetadata(RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "Windows" :
                RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? "MacOS" : "Linux");
            CheckStrings();
            CheckDictionary();
            CheckCallbacks();
            CheckArraysAndStructs();
            CheckLayout();
            Equal(0, Probe("smoke_outstanding_allocations"), "all native allocations released");
            Console.WriteLine("PASS native interop smoke: " + _assertions + " assertions.");
            Console.WriteLine("This .NET host is not a Unity Mono or IL2CPP player build.");
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            return 1;
        }
    }

    private static void CheckMetadata(string platform)
    {
        var assembly = typeof(ffmpeg).Assembly;
        var imports = assembly.GetTypes()
            .SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static))
            .Where(method => method.GetCustomAttribute<DllImportAttribute>() != null).ToArray();
        True(imports.Length >= 730, "full FFmpeg API imported");

        foreach (var method in imports)
        {
            var import = method.GetCustomAttribute<DllImportAttribute>();
            Equal(CallingConvention.Cdecl, import.CallingConvention, method.Name + " calling convention");
            True(import.ExactSpelling, method.Name + " exact symbol spelling");
            True(method.ReturnType != typeof(string), method.Name + " borrowed return stays unmanaged");
            foreach (var parameter in method.GetParameters())
            {
                True(parameter.ParameterType != typeof(string) && !parameter.ParameterType.IsArray,
                    method.Name + " uses explicit string/array marshalling");
                True(!parameter.ParameterType.Name.EndsWith("_func", StringComparison.Ordinal),
                    method.Name + " callback argument is a native pointer");
                True(parameter.GetCustomAttribute<MarshalAsAttribute>() == null,
                    method.Name + " does not require runtime custom marshalling");
            }
            True(method.ReturnParameter.GetCustomAttribute<MarshalAsAttribute>() == null,
                method.Name + " has no return marshalling");
        }

        var avutil = imports.Single(method =>
        {
            var import = method.GetCustomAttribute<DllImportAttribute>();
            return (import.EntryPoint ?? method.Name) == "av_version_info";
        }).GetCustomAttribute<DllImportAttribute>();
        string expectedLibrary = platform == "IOS" ? "__Internal" : platform == "Android" ? "avutil" :
            platform == "Linux" ? "libavutil.so.61" : platform == "MacOS" ? "libavutil.61.dylib" : "avutil-61";
        Equal(expectedLibrary, avutil.Value, platform + " native library selection");

        foreach (var type in assembly.GetTypes().Where(type => typeof(MulticastDelegate).IsAssignableFrom(type)))
        {
            var unmanaged = type.GetCustomAttribute<UnmanagedFunctionPointerAttribute>();
            True(unmanaged != null, type.Name + " callback convention declared");
            Equal(CallingConvention.Cdecl, unmanaged.CallingConvention, type.Name + " callback convention");
            foreach (var parameter in type.GetMethod("Invoke").GetParameters())
            {
                True(parameter.ParameterType != typeof(string) && !parameter.ParameterType.IsArray,
                    type.Name + " callback uses raw pointers");
                True(parameter.GetCustomAttribute<MarshalAsAttribute>() == null,
                    type.Name + " callback has no runtime marshaller");
            }
        }

        var checksumType = typeof(AVIOContext).GetField("checksum").FieldType;
        bool windows = platform == "Windows" || platform == "EditorAndroid" || platform == "EditorIOS";
        Equal(windows ? typeof(uint) : typeof(ulong), checksumType, platform + " C unsigned long width");
        Equal(IntPtr.Size, Marshal.SizeOf<av_buffer_create_free_func>(), "callback holder is one pointer");
        Equal(platform == "MacOS" || platform == "IOS" ? 35 : 11, ffmpeg.EAGAIN, platform + " errno selection");
    }

    private static void CheckStrings()
    {
        for (int i = 0; i < 256; ++i)
            Equal("stub-中文-✓", ffmpeg.av_version_info(), "borrowed UTF-8 return remains valid");
        Equal(0, Probe("smoke_outstanding_allocations"), "borrowed strings are not owned");
        byte* text = ffmpeg.av_strdup(UnicodeText);
        try
        {
            Equal(UnicodeText, Read(text), "UTF-8 input preserves multibyte and supplementary characters");
            Equal(UnicodeText, ffmpeg.PtrToStringUTF8(text), "public UTF-8 pointer helper");
        }
        finally { ffmpeg.av_free(text); }
        True(ffmpeg.PtrToStringUTF8(null) == null, "UTF-8 pointer helper accepts null");
        True(ffmpeg.av_strdup(null) == null, "null string becomes null pointer");
        text = ffmpeg.av_strdup(string.Empty);
        try { True(text != null && text[0] == 0, "empty string remains distinct from null"); }
        finally { ffmpeg.av_free(text); }
    }

    private static void CheckDictionary()
    {
        AVDictionary* dictionary = null;
        try
        {
            Equal(0, ffmpeg.av_dict_set(&dictionary, "键😀", UnicodeText, 0), "dictionary set");
            var entry = ffmpeg.av_dict_get(dictionary, "键😀", null, 0);
            True(entry != null, "UTF-8 dictionary lookup");
            Equal("键😀", Read(entry->key), "dictionary key round trip");
            Equal(UnicodeText, Read(entry->value), "dictionary value round trip");
            for (int i = 0; i < 128; ++i)
                Equal(-12, ffmpeg.av_dict_set(&dictionary, "__error__", UnicodeText, 0), "native failure returned unchanged");
            Equal(3, Probe("smoke_outstanding_allocations"), "error path retains only existing dictionary");
            Equal(0, ffmpeg.av_dict_set(&dictionary, "键😀", null, 0), "null value deletes entry");
            True(dictionary == null, "null value reached native API");
            Equal(-22, ffmpeg.av_dict_set(&dictionary, null, UnicodeText, 0), "null key reached native API");

            bool rejected = false;
            try { ffmpeg.av_dict_set(&dictionary, "key", "value", ffmpeg.AV_DICT_DONT_STRDUP_KEY); }
            catch (ArgumentException) { rejected = true; }
            True(rejected, "managed string ownership transfer rejected");
            rejected = false;
            try { ffmpeg.av_dict_set(&dictionary, "key", "value", ffmpeg.AV_DICT_DONT_STRDUP_VAL); }
            catch (ArgumentException) { rejected = true; }
            True(rejected, "managed value ownership transfer rejected");
            rejected = false;
            try { ffmpeg.av_dict_set_int(&dictionary, "key", 42, ffmpeg.AV_DICT_DONT_STRDUP_KEY); }
            catch (ArgumentException) { rejected = true; }
            True(rejected, "integer dictionary API also rejects managed key transfer");

            byte* key = ffmpeg.av_strdup("raw-key-键");
            byte* value = ffmpeg.av_strdup(UnicodeText);
            Equal(0, ffmpeg.av_dict_set_utf8(&dictionary, key, value,
                ffmpeg.AV_DICT_DONT_STRDUP_KEY | ffmpeg.AV_DICT_DONT_STRDUP_VAL), "raw API accepts native ownership transfer");
            entry = ffmpeg.av_dict_get(dictionary, "raw-key-键", null, 0);
            True(entry != null, "raw dictionary lookup");
            Equal(UnicodeText, Read(entry->value), "raw UTF-8 round trip");
        }
        finally { ffmpeg.av_dict_free(&dictionary); }
        Equal(0, Probe("smoke_outstanding_allocations"), "dictionary ownership balanced");
    }

    private static void CheckCallbacks()
    {
        byte* data = stackalloc byte[4];
        data[0] = 91;
        AVBufferRef* buffer = ffmpeg.av_buffer_create(data, 4, BufferCallback, (void*)0x4321, 0);
        True(buffer != null && buffer->data == data && buffer->size == 4, "buffer struct returned by native code");
        GC.Collect();
        GC.WaitForPendingFinalizers();
        ffmpeg.av_buffer_unref(&buffer);
        True(buffer == null, "native pointer-to-pointer mutation");
        Equal(1, _bufferCalls, "native reverse callback invoked once");
        Equal((byte)91, _bufferByte, "callback data pointer");
        Equal(new IntPtr(0x4321), _bufferOpaque, "callback opaque pointer");

        ffmpeg.av_log_set_callback(LogCallback);
        try
        {
            GC.Collect();
            Marshal.GetDelegateForFunctionPointer<NativeVoid>(NativeLibrary.GetExport(_native, "smoke_emit_log"))();
            Equal("log-中文-✓", _logText, "callback UTF-8 text");
            Equal(24, _logLevel, "callback integer argument");
            Equal(new IntPtr(0x1234), _logOpaque, "callback native pointer argument");
        }
        finally { ffmpeg.av_log_set_callback(default(av_log_set_callback_callback_func)); }
        GC.KeepAlive(BufferCallback);
        GC.KeepAlive(LogCallback);
    }

    private static void CheckArraysAndStructs()
    {
        byte* input = stackalloc byte[6];
        byte* output = stackalloc byte[9];
        for (int i = 0; i < 6; ++i) input[i] = (byte)(10 + i);
        for (int i = 0; i < 9; ++i) output[i] = 0;
        Equal(2, ffmpeg.sws_scale(null, new byte*[] { input }, new[] { 3 }, 1, 2,
            new byte*[] { output }, new[] { 3 }), "pinned arrays pass native pointers");
        for (int i = 0; i < 3; ++i) Equal((byte)0, output[i], "slice offset preserved");
        for (int i = 0; i < 6; ++i) Equal(input[i], output[i + 3], "native writes visible through arrays");
        Equal(-22, ffmpeg.sws_scale(null, null, null, 0, 0, null, null), "null arrays become null pointers");
        var sum = ffmpeg.av_add_q(new AVRational { num = 1, den = 3 }, new AVRational { num = 1, den = 5 });
        Equal(8, sum.num, "struct by-value return numerator");
        Equal(15, sum.den, "struct by-value return denominator");

        int_array9 matrix = default;
        matrix[0] = 2;
        matrix[8] = 7;
        Equal(9.0, ffmpeg.av_display_rotation_get(in matrix), "in fixed-array struct passes its address");
        ffmpeg.av_display_rotation_set(ref matrix, 31.0);
        Equal(31, matrix[0], "ref fixed-array first element written by native code");
        Equal(-31, matrix[8], "ref fixed-array final element written by native code");
    }

    private static void CheckLayout()
    {
        Equal(Probe("smoke_sizeof_long"), Marshal.SizeOf(typeof(AVIOContext).GetField("checksum").FieldType), "C unsigned long ABI");
        Equal(Probe("smoke_sizeof_avio"), sizeof(AVIOContext), "AVIOContext size against C compiler");
        Equal(Probe("smoke_offset_checksum"), Marshal.OffsetOf<AVIOContext>("checksum").ToInt32(), "checksum ABI offset");
        Equal(Probe("smoke_offset_checksum_ptr"), Marshal.OffsetOf<AVIOContext>("checksum_ptr").ToInt32(), "checksum_ptr ABI offset");
        Equal(Probe("smoke_offset_bytes_read"), Marshal.OffsetOf<AVIOContext>("bytes_read").ToInt32(), "bytes_read ABI offset");
    }

    private static int Probe(string name) =>
        Marshal.GetDelegateForFunctionPointer<NativeInt>(NativeLibrary.GetExport(_native, name))();

    private static void ReleaseBuffer(void* opaque, byte* data)
    {
        ++_bufferCalls;
        _bufferByte = data[0];
        _bufferOpaque = (IntPtr)opaque;
    }

    private static void ReceiveLog(void* opaque, int level, byte* text, byte* args)
    {
        _logOpaque = (IntPtr)opaque;
        _logLevel = level;
        _logText = Read(text);
    }

    private static string Read(byte* pointer)
    {
        if (pointer == null) return null;
        int length = 0;
        while (pointer[length] != 0) ++length;
        return Encoding.UTF8.GetString(pointer, length);
    }

    private static void True(bool condition, string message)
    {
        ++_assertions;
        if (!condition) throw new InvalidOperationException(message);
    }

    private static void Equal<T>(T expected, T actual, string message)
    {
        ++_assertions;
        if (!Equals(expected, actual))
            throw new InvalidOperationException(message + ": expected " + expected + ", got " + actual);
    }
}
