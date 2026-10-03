using System;
using System.Threading;
using AOT;
using UnityEngine;

namespace FFmpeg.AutoGen.Samples
{
    /// <summary>Attach to a scene object and run in both Mono and IL2CPP players.</summary>
    public sealed unsafe class NativeSmokeTest : MonoBehaviour
    {
        // The native function pointer does not keep its managed delegate alive.
        private static readonly av_buffer_create_free FreeCallback = FreeBuffer;
        private static int _freedBuffers;

        private void Start()
        {
            try
            {
                Run();
                Debug.Log("FFmpeg native smoke test passed: " + ffmpeg.av_version_info());
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }
        }

        public static void Run()
        {
            CheckVersion("avutil", ffmpeg.avutil_version());
            CheckVersion("avcodec", ffmpeg.avcodec_version());
            CheckVersion("avformat", ffmpeg.avformat_version());
            CheckVersion("avdevice", ffmpeg.avdevice_version());
            CheckVersion("avfilter", ffmpeg.avfilter_version());
            CheckVersion("swresample", ffmpeg.swresample_version());
            CheckVersion("swscale", ffmpeg.swscale_version());

            AVDictionary* dictionary = null;
            try
            {
                const string value = "Unity 中文 / 日本語 / 🎬";
                if (ffmpeg.av_dict_set(&dictionary, "title", value, 0) < 0)
                    throw new InvalidOperationException("av_dict_set failed.");
                var entry = ffmpeg.av_dict_get(dictionary, "title", null, 0);
                if (entry == null || ffmpeg.PtrToStringUTF8(entry->value) != value)
                    throw new InvalidOperationException("UTF-8 round trip failed.");
            }
            finally
            {
                ffmpeg.av_dict_free(&dictionary);
            }

            var sum = ffmpeg.av_add_q(new AVRational { num = 1, den = 2 }, new AVRational { num = 1, den = 3 });
            if (sum.num != 5 || sum.den != 6)
                throw new InvalidOperationException("AVRational by-value ABI failed.");
            var linesizes = new int_array4();
            if (ffmpeg.av_image_fill_linesizes(ref linesizes, AVPixelFormat.AV_PIX_FMT_RGBA, 16) < 0 || linesizes[0] != 64)
                throw new InvalidOperationException("Fixed array ABI failed.");

            var data = (byte*)ffmpeg.av_malloc(16);
            if (data == null)
                throw new OutOfMemoryException();
            var before = _freedBuffers;
            var buffer = ffmpeg.av_buffer_create(data, 16, FreeCallback, null, 0);
            if (buffer == null)
            {
                // av_buffer_create leaves data untouched on failure.
                ffmpeg.av_free(data);
                throw new OutOfMemoryException();
            }
            ffmpeg.av_buffer_unref(&buffer);
            if (_freedBuffers != before + 1)
                throw new InvalidOperationException("Native-to-managed callback failed.");
        }

        private static void CheckVersion(string library, uint version)
        {
            var expected = ffmpeg.LibraryVersionMap[library];
            if ((version >> 16) != expected)
                throw new InvalidOperationException(library + " ABI mismatch; expected major " + expected + ".");
        }

        [MonoPInvokeCallback(typeof(av_buffer_create_free))]
        private static void FreeBuffer(void* opaque, byte* data)
        {
            // Native callbacks can run on worker threads. Avoid Unity APIs here.
            ffmpeg.av_free(data);
            Interlocked.Increment(ref _freedBuffers);
        }
    }
}
