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
    private static readonly av_buffer_pool_init_alloc PoolCallback = AllocatePoolBuffer;
    private static nuint _poolSize;
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
            CheckNativeIntegers();
            CheckLayout();
            Equal(0, Probe("smoke_outstanding_allocations"), "all native allocations released");
            Console.WriteLine("PASS " + (IntPtr.Size * 8) + "-bit native interop smoke: " + _assertions + " assertions.");
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
        string expectedLibrary = platform == "IOS" ? "__Internal" : platform.StartsWith("Android", StringComparison.Ordinal) ? "avutil" :
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
        bool windows = platform.StartsWith("Windows", StringComparison.Ordinal) || platform == "EditorAndroid" || platform == "EditorIOS";
        Equal(windows ? typeof(uint) : typeof(nuint), checksumType, platform + " C unsigned long width");
        Equal(IntPtr.Size, Marshal.SizeOf<av_buffer_create_free_func>(), "callback holder is one pointer");
        Equal(platform == "MacOS" || platform == "IOS" ? 35 : 11, ffmpeg.EAGAIN, platform + " errno selection");
        Equal(typeof(nuint), typeof(AVBufferRef).GetField("size").FieldType, "size_t field uses native width");
        Equal(typeof(nuint), typeof(av_buffer_pool_init_alloc).GetMethod("Invoke").GetParameters()[0].ParameterType,
            "size_t reverse callback uses native width");
        Equal(typeof(long), typeof(AVPacket).GetField("pts").FieldType, "int64_t timestamps remain 64-bit");
        Equal(typeof(ulong), typeof(AVChannelLayout).GetField("u").FieldType.GetField("mask").FieldType,
            "uint64_t channel mask remains 64-bit");
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
        Equal(IntPtr.Size, Probe("smoke_sizeof_pointer"), "native library matches managed process bitness");
        Equal(IntPtr.Size, Probe("smoke_sizeof_size_t"), "size_t native width");
        Equal(IntPtr.Size, Probe("smoke_sizeof_ptrdiff_t"), "ptrdiff_t native width");
        Equal(Probe("smoke_sizeof_long"), Marshal.SizeOf(typeof(AVIOContext).GetField("checksum").FieldType), "C unsigned long ABI");
        Equal(Probe("smoke_sizeof_avio"), sizeof(AVIOContext), "AVIOContext size against C compiler");
        Equal(Probe("smoke_offset_checksum"), Marshal.OffsetOf<AVIOContext>("checksum").ToInt32(), "checksum ABI offset");
        Equal(Probe("smoke_offset_checksum_ptr"), Marshal.OffsetOf<AVIOContext>("checksum_ptr").ToInt32(), "checksum_ptr ABI offset");
        Equal(Probe("smoke_offset_bytes_read"), Marshal.OffsetOf<AVIOContext>("bytes_read").ToInt32(), "bytes_read ABI offset");
        Equal(Probe("smoke_sizeof_buffer_ref"), sizeof(AVBufferRef), "AVBufferRef size_t structure ABI");
        Equal(Probe("smoke_offset_buffer_size"), Marshal.OffsetOf<AVBufferRef>("size").ToInt32(), "AVBufferRef size field offset");
        Equal(Probe("smoke_sizeof_packet"), sizeof(AVPacket), "AVPacket fixed int64_t structure ABI");
        Equal(Probe("smoke_offset_packet_pts"), Marshal.OffsetOf<AVPacket>("pts").ToInt32(), "AVPacket pts offset");
        Equal(Probe("smoke_offset_packet_duration"), Marshal.OffsetOf<AVPacket>("duration").ToInt32(), "AVPacket duration offset");
        Equal(Probe("smoke_offset_packet_time_base"), Marshal.OffsetOf<AVPacket>("time_base").ToInt32(), "AVPacket time_base offset");
        Equal(Probe("smoke_sizeof_packet_side_data"), sizeof(AVPacketSideData), "AVPacketSideData ABI");
        Equal(Probe("smoke_offset_packet_side_data_type"), Marshal.OffsetOf<AVPacketSideData>("type").ToInt32(), "AVPacketSideData type after size_t");
        Equal(Probe("smoke_sizeof_frame_side_data"), sizeof(AVFrameSideData), "AVFrameSideData ABI");
        Equal(Probe("smoke_offset_frame_side_data_buffer"), Marshal.OffsetOf<AVFrameSideData>("buf").ToInt32(), "AVFrameSideData buffer after size_t");
        Equal(Probe("smoke_sizeof_frame"), sizeof(AVFrame), "AVFrame ABI");
        Equal(Probe("smoke_offset_frame_crop_top"), Marshal.OffsetOf<AVFrame>("crop_top").ToInt32(), "AVFrame first size_t crop field");
        Equal(Probe("smoke_offset_frame_crop_right"), Marshal.OffsetOf<AVFrame>("crop_right").ToInt32(), "AVFrame final size_t crop field");
        Equal(Probe("smoke_offset_frame_channel_layout"), Marshal.OffsetOf<AVFrame>("ch_layout").ToInt32(), "AVFrame field after size_t crops");
        Equal(Probe("smoke_sizeof_codec_context"), sizeof(AVCodecContext), "AVCodecContext ABI");
        Equal(Probe("smoke_offset_codec_bit_rate"), Marshal.OffsetOf<AVCodecContext>("bit_rate").ToInt32(), "AVCodecContext int64_t bit rate offset");
        Equal(Probe("smoke_offset_codec_extradata"), Marshal.OffsetOf<AVCodecContext>("extradata").ToInt32(), "AVCodecContext pointer after int64_t");
        Equal(Probe("smoke_offset_codec_frame_num"), Marshal.OffsetOf<AVCodecContext>("frame_num").ToInt32(), "AVCodecContext trailing int64_t frame count");
        Equal(Probe("smoke_offset_codec_decoded_side_data"), Marshal.OffsetOf<AVCodecContext>("decoded_side_data").ToInt32(), "AVCodecContext trailing pointer offset");
        Equal(Probe("smoke_sizeof_format_context"), sizeof(AVFormatContext), "AVFormatContext ABI");
        Equal(Probe("smoke_offset_format_streams"), Marshal.OffsetOf<AVFormatContext>("streams").ToInt32(), "AVFormatContext streams pointer offset");
        Equal(Probe("smoke_offset_format_duration"), Marshal.OffsetOf<AVFormatContext>("duration").ToInt32(), "AVFormatContext int64_t duration offset");
        Equal(Probe("smoke_offset_format_control_message_cb"), Marshal.OffsetOf<AVFormatContext>("control_message_cb").ToInt32(), "AVFormatContext callback offset");
        Equal(Probe("smoke_offset_format_dump_separator"), Marshal.OffsetOf<AVFormatContext>("dump_separator").ToInt32(), "AVFormatContext pointer after int64_t offset");
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SizeOutput
    {
        internal nuint Before;
        internal nuint Size;
        internal nuint After;
    }

    private static void CheckNativeIntegers()
    {
        byte* allocated = (byte*)ffmpeg.av_malloc((nuint)19);
        True(allocated != null, "av_malloc accepts size_t and returns pointer");
        allocated[0] = 12;
        allocated[18] = 34;
        Equal((byte)34, allocated[18], "allocation is writable at requested end");
        ffmpeg.av_free(allocated);
        Equal((nuint)0xf1234567U, ffmpeg.av_cpu_max_align(), "unsigned size_t return preserves high bit");

        SizeOutput size = new SizeOutput { Before = (nuint)0xa1234567U, Size = nuint.MaxValue, After = (nuint)0xb1234567U };
        byte* mapped = null;
        Equal(0, ffmpeg.av_file_map("map-中文", &mapped, &size.Size, 0x2468, (void*)0x3456), "size_t output and following arguments");
        try
        {
            Equal((nuint)6, size.Size, "size_t pointer writes exactly native width");
            Equal((nuint)0xa1234567U, size.Before, "size_t output leading sentinel");
            Equal((nuint)0xb1234567U, size.After, "size_t output trailing sentinel");
            Equal("mapped", Read(mapped), "mapped bytes");
        }
        finally { ffmpeg.av_file_unmap(mapped, size.Size); }

        AVBufferPool* pool = ffmpeg.av_buffer_pool_init((nuint)0xf1234567U, PoolCallback);
        True(pool != null, "buffer pool allocated");
        AVBufferRef* buffer = null;
        try
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            buffer = ffmpeg.av_buffer_pool_get(pool);
            Equal((nuint)0xf1234567U, _poolSize, "size_t reverse callback argument preserves high bit");
            True(buffer != null, "size_t callback returns native struct pointer");
            Equal((nuint)0xf1234567U, buffer->size, "size_t field round trip through native allocation");
        }
        finally
        {
            ffmpeg.av_buffer_unref(&buffer);
            ffmpeg.av_buffer_pool_uninit(&pool);
            GC.KeepAlive(PoolCallback);
        }
        True(pool == null, "buffer pool pointer cleared");

        byte* input = stackalloc byte[16];
        byte* output = stackalloc byte[16];
        for (int i = 0; i < 16; ++i) { input[i] = (byte)(20 + i); output[i] = 0; }
        ffmpeg.av_image_copy_plane_uc_from(output, (nint)3, input + 3, (nint)(-3), (nint)2, 2);
        Equal(input[3], output[0], "signed ptrdiff_t first row");
        Equal(input[0], output[3], "negative ptrdiff_t stride reaches preceding row");
        byte_ptrArray4 sources = default;
        byte_ptrArray4 destinations = default;
        nint_array4 sourceStrides = default;
        nint_array4 destinationStrides = default;
        sources[0] = input; sources[1] = input + 8;
        destinations[0] = output; destinations[1] = output + 8;
        sourceStrides[0] = 3; sourceStrides[1] = 4;
        destinationStrides[0] = 3; destinationStrides[1] = 4;
        ffmpeg.av_image_copy_uc_from(ref destinations, in destinationStrides, in sources, in sourceStrides,
            AVPixelFormat.AV_PIX_FMT_GRAY8, 2, 2);
        Equal(input[3], output[3], "ptrdiff_t array first plane stride");
        Equal(input[12], output[12], "ptrdiff_t array second element has native width");
        Equal(IntPtr.Size * 4, sizeof(nint_array4), "ptrdiff_t fixed array size");
        Equal(IntPtr.Size * 4, sizeof(nuint_array4), "size_t fixed array size");
        const long timestamp = 0x12345678abcdef;
        Equal(timestamp * 2, ffmpeg.av_rescale_q(timestamp, new AVRational { num = 2, den = 1 },
            new AVRational { num = 1, den = 1 }), "int64_t arguments and returns retain upper bits on 32-bit hosts");
    }

    private static AVBufferRef* AllocatePoolBuffer(nuint size)
    {
        _poolSize = size;
        return ffmpeg.av_buffer_create(null, size, default(av_buffer_create_free_func), null, 0);
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
