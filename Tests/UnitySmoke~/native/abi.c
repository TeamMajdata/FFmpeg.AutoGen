/* Compile ABI probes from the actual checked-in FFmpeg headers. */
#include <stddef.h>
#include <libavutil/buffer.h>
#include <libavutil/frame.h>
#include <libavcodec/packet.h>
#include <libavcodec/avcodec.h>
#include <libavformat/avio.h>
#include <libavformat/avformat.h>

#if defined(_WIN32)
#define EXPORT __declspec(dllexport)
#else
#define EXPORT __attribute__((visibility("default")))
#endif

EXPORT int smoke_sizeof_long(void) { return (int)sizeof(unsigned long); }
EXPORT int smoke_sizeof_avio(void) { return (int)sizeof(AVIOContext); }
EXPORT int smoke_offset_checksum(void) { return (int)offsetof(AVIOContext, checksum); }
EXPORT int smoke_offset_checksum_ptr(void) { return (int)offsetof(AVIOContext, checksum_ptr); }
EXPORT int smoke_offset_bytes_read(void) { return (int)offsetof(AVIOContext, bytes_read); }
EXPORT int smoke_sizeof_pointer(void) { return (int)sizeof(void *); }
EXPORT int smoke_sizeof_size_t(void) { return (int)sizeof(size_t); }
EXPORT int smoke_sizeof_ptrdiff_t(void) { return (int)sizeof(ptrdiff_t); }
EXPORT int smoke_sizeof_buffer_ref(void) { return (int)sizeof(AVBufferRef); }
EXPORT int smoke_offset_buffer_size(void) { return (int)offsetof(AVBufferRef, size); }
EXPORT int smoke_sizeof_packet(void) { return (int)sizeof(AVPacket); }
EXPORT int smoke_offset_packet_pts(void) { return (int)offsetof(AVPacket, pts); }
EXPORT int smoke_offset_packet_duration(void) { return (int)offsetof(AVPacket, duration); }
EXPORT int smoke_offset_packet_time_base(void) { return (int)offsetof(AVPacket, time_base); }
EXPORT int smoke_sizeof_packet_side_data(void) { return (int)sizeof(AVPacketSideData); }
EXPORT int smoke_offset_packet_side_data_type(void) { return (int)offsetof(AVPacketSideData, type); }
EXPORT int smoke_sizeof_frame_side_data(void) { return (int)sizeof(AVFrameSideData); }
EXPORT int smoke_offset_frame_side_data_buffer(void) { return (int)offsetof(AVFrameSideData, buf); }
EXPORT int smoke_sizeof_frame(void) { return (int)sizeof(AVFrame); }
EXPORT int smoke_offset_frame_crop_top(void) { return (int)offsetof(AVFrame, crop_top); }
EXPORT int smoke_offset_frame_crop_right(void) { return (int)offsetof(AVFrame, crop_right); }
EXPORT int smoke_offset_frame_channel_layout(void) { return (int)offsetof(AVFrame, ch_layout); }
EXPORT int smoke_sizeof_codec_context(void) { return (int)sizeof(AVCodecContext); }
EXPORT int smoke_offset_codec_bit_rate(void) { return (int)offsetof(AVCodecContext, bit_rate); }
EXPORT int smoke_offset_codec_extradata(void) { return (int)offsetof(AVCodecContext, extradata); }
EXPORT int smoke_offset_codec_frame_num(void) { return (int)offsetof(AVCodecContext, frame_num); }
EXPORT int smoke_offset_codec_decoded_side_data(void) { return (int)offsetof(AVCodecContext, decoded_side_data); }
EXPORT int smoke_sizeof_format_context(void) { return (int)sizeof(AVFormatContext); }
EXPORT int smoke_offset_format_streams(void) { return (int)offsetof(AVFormatContext, streams); }
EXPORT int smoke_offset_format_duration(void) { return (int)offsetof(AVFormatContext, duration); }
EXPORT int smoke_offset_format_control_message_cb(void) { return (int)offsetof(AVFormatContext, control_message_cb); }
EXPORT int smoke_offset_format_dump_separator(void) { return (int)offsetof(AVFormatContext, dump_separator); }
