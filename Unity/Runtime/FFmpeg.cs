using System;

namespace FFmpeg.AutoGen
{
    public static unsafe partial class ffmpeg
    {
#if UNITY_EDITOR_OSX
        public static readonly int EAGAIN = 35;
#elif UNITY_EDITOR
        public static readonly int EAGAIN = 11;
#elif UNITY_STANDALONE_OSX || UNITY_IOS
        public static readonly int EAGAIN = 35;
#else
        public static readonly int EAGAIN = 11;
#endif

        public static readonly int ENOMEM = 12;
        public static readonly int EINVAL = 22;
        public static readonly int EPIPE = 32;

        static ffmpeg()
        {
            if (IntPtr.Size != 8)
            {
                throw new PlatformNotSupportedException(
                    "FFmpeg.AutoGen Unity bindings require a 64-bit process and matching 64-bit FFmpeg libraries.");
            }
        }

        /// <summary>
        /// Copies a null-terminated UTF-8 string from native memory without freeing it.
        /// A null pointer returns null. The pointer must remain valid for this call.
        /// </summary>
        public static string PtrToStringUTF8(byte* value)
        {
            return Utf8String.Read(value);
        }

        public static ulong UINT64_C<T>(T a)
            => Convert.ToUInt64(a);

        public static int AVERROR<T1>(T1 a)
            => -Convert.ToInt32(a);

        public static int MKTAG<T1, T2, T3, T4>(T1 a, T2 b, T3 c, T4 d)
            => (int)(Convert.ToUInt32(a) | (Convert.ToUInt32(b) << 8) | (Convert.ToUInt32(c) << 16) |
                     (Convert.ToUInt32(d) << 24));

        public static int FFERRTAG<T1, T2, T3, T4>(T1 a, T2 b, T3 c, T4 d)
            => -MKTAG(a, b, c, d);

        public static int AV_VERSION_INT<T1, T2, T3>(T1 a, T2 b, T3 c)
            => (Convert.ToInt32(a) << 16) | (Convert.ToInt32(b) << 8) | Convert.ToInt32(c);

        public static string AV_VERSION_DOT<T1, T2, T3>(T1 a, T2 b, T3 c)
            => $"{a}.{b}.{c}";

        public static string AV_VERSION<T1, T2, T3>(T1 a, T2 b, T3 c)
            => AV_VERSION_DOT(a, b, c);
    }
}
