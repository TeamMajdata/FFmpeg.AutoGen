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
    private static int freeCalls;
    private static string logMessage;

    [DllImport("avutil-61", CallingConvention = CallingConvention.Cdecl)]
    private static extern void smoke_emit_log();
    [DllImport("avutil-61", CallingConvention = CallingConvention.Cdecl)]
    private static extern int smoke_sizeof_avio();
    [DllImport("avutil-61", CallingConvention = CallingConvention.Cdecl)]
    private static extern int smoke_offset_checksum();
    [DllImport("avutil-61", CallingConvention = CallingConvention.Cdecl)]
    private static extern int smoke_offset_checksum_ptr();
    [DllImport("avutil-61", CallingConvention = CallingConvention.Cdecl)]
    private static extern int smoke_offset_bytes_read();

    public static void Run()
    {
        AVIOContext io = default;
        if (sizeof(AVIOContext) != smoke_sizeof_avio() ||
            (byte*)&io.checksum - (byte*)&io != smoke_offset_checksum() ||
            (byte*)&io.checksum_ptr - (byte*)&io != smoke_offset_checksum_ptr() ||
            (byte*)&io.bytes_read - (byte*)&io != smoke_offset_bytes_read())
            throw new Exception("AVIOContext native layout");
        if (sizeof(byte_ptrArray8) != 64 || sizeof(int_array8) != 32)
            throw new Exception("Fixed array native layout");
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

        byte* source = stackalloc byte[4];
        byte* destination = stackalloc byte[4];
        for (int i = 0; i < 4; i++) source[i] = (byte)(i + 10);
        int scaled = ffmpeg.sws_scale(null, new byte*[] { source }, new[] { 2 }, 0, 2,
            new byte*[] { destination }, new[] { 2 });
        if (scaled != 2) throw new Exception("Pinned array return");
        for (int i = 0; i < 4; i++)
            if (source[i] != destination[i]) throw new Exception("Pinned array contents");

        freeCalls = 0;
        AVBufferRef* buffer = ffmpeg.av_buffer_create(source, 4, FreeCallback, (void*)123, 0);
        if (buffer == null) throw new Exception("Callback buffer allocation");
        GC.Collect();
        GC.WaitForPendingFinalizers();
        ffmpeg.av_buffer_unref(&buffer);
        if (buffer != null || freeCalls != 1) throw new Exception("Reverse P/Invoke callback");

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
        try { Run(); report = "PASS: native layout, fixed arrays, UTF-8, null, AVRational, pinned arrays, AOT callbacks"; }
        catch (Exception exception) { exitCode = 1; report = exception.ToString(); }
        Debug.Log(report);
        string[] arguments = Environment.GetCommandLineArgs();
        for (int i = 0; i + 1 < arguments.Length; i++)
            if (arguments[i] == "-smokeReport") File.WriteAllText(arguments[i + 1], report);
        Application.Quit(exitCode);
    }
}
