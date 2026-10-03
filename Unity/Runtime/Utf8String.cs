using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace FFmpeg.AutoGen
{
    /// <summary>
    /// Owns the UTF-8 buffer used for a single native function call.
    /// </summary>
    internal sealed unsafe class Utf8String : IDisposable
    {
        private IntPtr _allocation;

        public Utf8String(string value)
        {
            if (value == null)
            {
                return;
            }

            byte[] bytes = Encoding.UTF8.GetBytes(value);
            IntPtr allocation = Marshal.AllocHGlobal(checked(bytes.Length + 1));
            try
            {
                Marshal.Copy(bytes, 0, allocation, bytes.Length);
                Marshal.WriteByte(allocation, bytes.Length, 0);
                _allocation = allocation;
            }
            catch
            {
                Marshal.FreeHGlobal(allocation);
                throw;
            }
        }

        public byte* Pointer => (byte*)_allocation.ToPointer();

        /// <summary>
        /// Reads borrowed native memory; ownership remains with the caller.
        /// </summary>
        public static string Read(byte* value)
        {
            if (value == null)
            {
                return null;
            }

            int length = 0;
            while (value[length] != 0)
            {
                length = checked(length + 1);
            }

            return Encoding.UTF8.GetString(value, length);
        }

        public void Dispose()
        {
            IntPtr allocation = Interlocked.Exchange(ref _allocation, IntPtr.Zero);
            if (allocation != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(allocation);
            }
        }
    }
}
