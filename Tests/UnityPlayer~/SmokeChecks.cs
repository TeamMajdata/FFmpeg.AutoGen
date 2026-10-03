using System;
using System.IO;
using System.Runtime.InteropServices;
using AOT;
using FFmpeg.AutoGen;
using UnityEngine;

public static unsafe class SmokeChecks
{
    private static readonly av_buffer_create_free FreeCallback = OnFree;
    private static readonly av_log_set_callback_callback LogCallback = OnLog;
    private static readonly av_buffer_pool_init_alloc PoolCallback = OnPoolAllocate;
    private static int freeCalls;
    private static string logMessage;
    private static nuint poolSize;
#if UNITY_ANDROID && !UNITY_EDITOR
    private const string NativeUtil = "avutil";
#else
    private const string NativeUtil = "avutil-61";
#endif

    [DllImport(NativeUtil, CallingConvention = CallingConvention.Cdecl)]
    private static extern void smoke_emit_log();
    [DllImport(NativeUtil, CallingConvention = CallingConvention.Cdecl)]
    private static extern int smoke_sizeof_avio();
    [DllImport(NativeUtil, CallingConvention = CallingConvention.Cdecl)]
    private static extern int smoke_offset_checksum();
    [DllImport(NativeUtil, CallingConvention = CallingConvention.Cdecl)]
    private static extern int smoke_offset_checksum_ptr();
    [DllImport(NativeUtil, CallingConvention = CallingConvention.Cdecl)]
    private static extern int smoke_offset_bytes_read();
    [DllImport(NativeUtil, CallingConvention = CallingConvention.Cdecl)]
    private static extern int smoke_sizeof_pointer();
    [DllImport(NativeUtil, CallingConvention = CallingConvention.Cdecl)]
    private static extern int smoke_sizeof_buffer_ref();
    [DllImport(NativeUtil, CallingConvention = CallingConvention.Cdecl)]
    private static extern int smoke_offset_buffer_size();
    [DllImport(NativeUtil, CallingConvention = CallingConvention.Cdecl)]
    private static extern int smoke_sizeof_packet();
    [DllImport(NativeUtil, CallingConvention = CallingConvention.Cdecl)]
    private static extern int smoke_offset_packet_pts();
    [DllImport(NativeUtil, CallingConvention = CallingConvention.Cdecl)]
    private static extern int smoke_offset_packet_duration();
    [DllImport(NativeUtil, CallingConvention = CallingConvention.Cdecl)]
    private static extern int smoke_offset_packet_time_base();

    public static void Run()
    {
        AVIOContext io = default;
        if (sizeof(AVIOContext) != smoke_sizeof_avio() ||
            (byte*)&io.checksum - (byte*)&io != smoke_offset_checksum() ||
            (byte*)&io.checksum_ptr - (byte*)&io != smoke_offset_checksum_ptr() ||
            (byte*)&io.bytes_read - (byte*)&io != smoke_offset_bytes_read())
            throw new Exception("AVIOContext native layout");
        if (IntPtr.Size != smoke_sizeof_pointer() || sizeof(byte_ptrArray8) != IntPtr.Size * 8 || sizeof(int_array8) != 32 ||
            sizeof(nint_array4) != IntPtr.Size * 4 || sizeof(nuint_array4) != IntPtr.Size * 4)
            throw new Exception("Fixed array native layout");
        AVBufferRef bufferLayout = default;
        AVPacket packetLayout = default;
        if (sizeof(AVBufferRef) != smoke_sizeof_buffer_ref() ||
            (byte*)&bufferLayout.size - (byte*)&bufferLayout != smoke_offset_buffer_size() ||
            sizeof(AVPacket) != smoke_sizeof_packet() ||
            (byte*)&packetLayout.pts - (byte*)&packetLayout != smoke_offset_packet_pts() ||
            (byte*)&packetLayout.duration - (byte*)&packetLayout != smoke_offset_packet_duration() ||
            (byte*)&packetLayout.time_base - (byte*)&packetLayout != smoke_offset_packet_time_base())
            throw new Exception("Pointer-sized fields and 64-bit timestamp layout");
        int_array9 matrix = default;
        matrix[0] = 4;
        matrix[8] = 5;
        if (ffmpeg.av_display_rotation_get(in matrix) != 9)
            throw new Exception("Fixed array by-reference ABI");
        Equal("stub-中文-✓", ffmpeg.av_version_info(), "UTF-8 return");
        byte* value = ffmpeg.av_strdup("input-中文-✓");
        try { Equal("input-中文-✓", ffmpeg.PtrToStringUTF8(value), "UTF-8 argument"); }
        finally { ffmpeg.av_free(value); }
        if (ffmpeg.av_strdup(null) != null) throw new Exception("Null string argument");

        var sum = ffmpeg.av_add_q(new AVRational { num = 1, den = 2 }, new AVRational { num = 1, den = 3 });
        if (sum.num * 6 != sum.den * 5) throw new Exception("AVRational by-value ABI");
        const long timestamp = 0x100000001L;
        if (ffmpeg.av_rescale_q(timestamp, new AVRational { num = 3, den = 1 }, new AVRational { num = 1, den = 1 }) != timestamp * 3)
            throw new Exception("64-bit timestamp by-value ABI");
        // UIntPtr's uint constructor avoids IL2CPP 6000.3's sign extension of
        // the C# `(nuint)uintConstant` conv.u sequence when bit 31 is set.
        nuint largeNativeSize = new UIntPtr(0xf1234567U);
        nuint returnedAlignment = ffmpeg.av_cpu_max_align();
        if (returnedAlignment != largeNativeSize)
            throw new Exception("Unsigned native-sized return: " + ((UIntPtr)returnedAlignment).ToUInt64() +
                "; expected " + ((UIntPtr)largeNativeSize).ToUInt64());
        byte* mapped = null;
        nuint mappedSize = 0;
        if (ffmpeg.av_file_map("map", &mapped, &mappedSize, 0x2468, (void*)0x3456) != 0 || mapped == null)
            throw new Exception("Native-sized out pointer and trailing arguments");
        try
        {
            if (mappedSize != 6 || mapped[0] != (byte)'m') throw new Exception("Mapped size_t value");
        }
        finally { ffmpeg.av_file_unmap(mapped, mappedSize); }

        byte* source = stackalloc byte[4];
        byte* destination = stackalloc byte[4];
        for (int i = 0; i < 4; i++) source[i] = (byte)(i + 10);
        int scaled = ffmpeg.sws_scale(null, new byte*[] { source }, new[] { 2 }, 0, 2,
            new byte*[] { destination }, new[] { 2 });
        if (scaled != 2) throw new Exception("Pinned array return");
        for (int i = 0; i < 4; i++)
            if (source[i] != destination[i]) throw new Exception("Pinned array contents");
        ffmpeg.av_image_copy_plane_uc_from(destination, 2, source + 2, -2, 2, 2);
        if (destination[0] != 12 || destination[2] != 10) throw new Exception("Signed native-sized stride");
        var sourcePlanes = new byte_ptrArray4();
        var destinationPlanes = new byte_ptrArray4();
        var sourceStrides = new nint_array4();
        var destinationStrides = new nint_array4();
        sourcePlanes[0] = source;
        sourcePlanes[1] = source + 2;
        destinationPlanes[0] = destination;
        destinationPlanes[1] = destination + 2;
        sourceStrides[0] = sourceStrides[1] = 2;
        destinationStrides[0] = destinationStrides[1] = 2;
        ffmpeg.av_image_copy_uc_from(ref destinationPlanes, in destinationStrides, in sourcePlanes, in sourceStrides,
            AVPixelFormat.AV_PIX_FMT_GRAY8, 2, 1);
        for (int i = 0; i < 4; i++)
            if (source[i] != destination[i]) throw new Exception("Native-sized stride array");

        freeCalls = 0;
        AVBufferRef* buffer = ffmpeg.av_buffer_create(source, 4, FreeCallback, (void*)123, 0);
        if (buffer == null) throw new Exception("Callback buffer allocation");
        GC.Collect();
        GC.WaitForPendingFinalizers();
        ffmpeg.av_buffer_unref(&buffer);
        if (buffer != null || freeCalls != 1) throw new Exception("Reverse P/Invoke callback");
        poolSize = 0;
        var pool = ffmpeg.av_buffer_pool_init(largeNativeSize, PoolCallback);
        if (pool == null) throw new Exception("Buffer pool allocation");
        try
        {
            var pooled = ffmpeg.av_buffer_pool_get(pool);
            if (pooled == null) throw new Exception("Native-sized callback allocation");
            try
            {
                if (poolSize != largeNativeSize || pooled->size != poolSize)
                    throw new Exception("Native-sized reverse callback parameter");
            }
            finally { ffmpeg.av_buffer_unref(&pooled); }
        }
        finally { ffmpeg.av_buffer_pool_uninit(&pool); }

        logMessage = null;
        ffmpeg.av_log_set_callback(LogCallback);
        smoke_emit_log();
        ffmpeg.av_log_set_callback(default(av_log_set_callback_callback_func));
        Equal("log-中文-✓", logMessage, "UTF-8 reverse callback");
    }

    [MonoPInvokeCallback(typeof(av_buffer_create_free))]
    private static void OnFree(void* opaque, byte* data)
    {
        // Native callbacks must not let managed exceptions cross the ABI boundary.
        freeCalls += opaque == (void*)123 && data != null && data[0] == 10 ? 1 : -100;
    }

    [MonoPInvokeCallback(typeof(av_log_set_callback_callback))]
    private static void OnLog(void* context, int level, byte* format, byte* args)
    {
        logMessage = ffmpeg.PtrToStringUTF8(format);
    }

    [MonoPInvokeCallback(typeof(av_buffer_pool_init_alloc))]
    private static AVBufferRef* OnPoolAllocate(nuint size)
    {
        poolSize = size;
        return ffmpeg.av_buffer_create(null, size, default(av_buffer_create_free_func), null, 0);
    }

    private static void Equal(string expected, string actual, string operation)
    {
        if (expected != actual) throw new Exception(operation + ": " + actual);
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void RunPlayer()
    {
        if (Application.isEditor) return;
        int exitCode = 0;
        string report;
        try { Run(); report = "PASS: pointer size=" + IntPtr.Size + "; native layout, fixed arrays, UTF-8, size_t, ptrdiff_t, int64_t, AVRational, pinned arrays, AOT callbacks"; }
        catch (Exception exception) { exitCode = 1; report = exception.ToString(); }
        Debug.Log(report);
#if UNITY_ANDROID
        File.WriteAllText(Path.Combine(Application.persistentDataPath, "smoke-report.txt"), report);
#endif
        string[] arguments = Environment.GetCommandLineArgs();
        for (int i = 0; i + 1 < arguments.Length; i++)
            if (arguments[i] == "-smokeReport") File.WriteAllText(arguments[i + 1], report);
        Application.Quit(exitCode);
    }
}
