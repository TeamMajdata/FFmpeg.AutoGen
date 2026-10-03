/* Test double only: this is deliberately not a codec library or FFmpeg implementation. */
#include <stddef.h>
#include <stdint.h>
#include <stdlib.h>
#include <string.h>

#if defined(_WIN32)
#define EXPORT __declspec(dllexport)
#else
#define EXPORT __attribute__((visibility("default")))
#endif

static const char utf8_version[] = "stub-\xe4\xb8\xad\xe6\x96\x87-\xe2\x9c\x93";
static const char utf8_log[] = "log-\xe4\xb8\xad\xe6\x96\x87-\xe2\x9c\x93";
static int outstanding_allocations;

static void *tracked_alloc(size_t size)
{
    void *p = malloc(size);
    if (p) ++outstanding_allocations;
    return p;
}

EXPORT void av_free(void *p)
{
    if (p) { --outstanding_allocations; free(p); }
}

EXPORT void *av_malloc(size_t size) { return tracked_alloc(size); }
EXPORT const char *av_version_info(void) { return utf8_version; }
EXPORT unsigned avutil_version(void) { return 61U << 16; }
EXPORT int smoke_outstanding_allocations(void) { return outstanding_allocations; }

EXPORT char *av_strdup(const char *s)
{
    if (!s) return NULL;
    size_t n = strlen(s) + 1;
    char *p = (char *)tracked_alloc(n);
    if (p) memcpy(p, s, n);
    return p;
}

typedef struct { char *key, *value; } DictionaryEntry;
typedef struct { DictionaryEntry entry; } Dictionary;

EXPORT void av_dict_free(Dictionary **pm)
{
    if (!pm || !*pm) return;
    av_free((*pm)->entry.key);
    av_free((*pm)->entry.value);
    av_free(*pm);
    *pm = NULL;
}

EXPORT int av_dict_set(Dictionary **pm, const char *key, const char *value, int flags)
{
    if (!pm || !key) return -22;
    if (!strcmp(key, "__error__")) return -12;
    if (!value) { av_dict_free(pm); return 0; }
    av_dict_free(pm);
    *pm = (Dictionary *)tracked_alloc(sizeof(Dictionary));
    if (!*pm) return -12;
    (*pm)->entry.key = flags & 4 ? (char *)key : av_strdup(key);
    (*pm)->entry.value = flags & 8 ? (char *)value : av_strdup(value);
    if (!(*pm)->entry.key || !(*pm)->entry.value) { av_dict_free(pm); return -12; }
    return 0;
}

EXPORT DictionaryEntry *av_dict_get(Dictionary *m, const char *key, DictionaryEntry *prev, int flags)
{
    (void)flags;
    if (!m || !key || prev || strcmp(m->entry.key, key)) return NULL;
    return &m->entry;
}

EXPORT int av_dict_count(const Dictionary *m) { return m ? 1 : 0; }

typedef void (*buffer_free_callback)(void *, uint8_t *);
typedef struct {
    void *buffer;
    uint8_t *data;
    size_t size;
    buffer_free_callback release;
    void *opaque;
} BufferRef;

EXPORT BufferRef *av_buffer_create(uint8_t *data, size_t size, buffer_free_callback release, void *opaque, int flags)
{
    (void)flags;
    BufferRef *b = (BufferRef *)tracked_alloc(sizeof(BufferRef));
    if (!b) return NULL;
    b->buffer = NULL;
    b->data = data;
    b->size = size;
    b->release = release;
    b->opaque = opaque;
    return b;
}

EXPORT void av_buffer_unref(BufferRef **pb)
{
    if (!pb || !*pb) return;
    BufferRef *b = *pb;
    *pb = NULL;
    if (b->release) b->release(b->opaque, b->data);
    av_free(b);
}

typedef void (*log_callback)(void *, int, const char *, void *);
static log_callback log_sink;
EXPORT void av_log_set_callback(log_callback callback) { log_sink = callback; }
EXPORT void smoke_emit_log(void) { if (log_sink) log_sink((void *)(uintptr_t)0x1234, 24, utf8_log, NULL); }

EXPORT int sws_scale(void *ctx, const uint8_t *const src[], const int src_stride[], int y, int h,
                     uint8_t *const dst[], const int dst_stride[])
{
    (void)ctx;
    if (!src || !src_stride || !dst || !dst_stride || !src[0] || !dst[0]) return -22;
    if (y < 0 || h < 0 || src_stride[0] < 1 || dst_stride[0] < 1) return -22;
    int width = src_stride[0] < dst_stride[0] ? src_stride[0] : dst_stride[0];
    for (int row = 0; row < h; ++row)
        memcpy(dst[0] + (row + y) * dst_stride[0], src[0] + row * src_stride[0], (size_t)width);
    return h;
}

typedef struct { int num, den; } Rational;
EXPORT Rational av_add_q(Rational a, Rational b)
{
    Rational result = { a.num * b.den + b.num * a.den, a.den * b.den };
    return result;
}

EXPORT double av_display_rotation_get(const int32_t matrix[9]) { return matrix[0] + matrix[8]; }
EXPORT void av_display_rotation_set(int32_t matrix[9], double angle)
{
    matrix[0] = (int32_t)angle;
    matrix[8] = -(int32_t)angle;
}

/* Mirrors the checked-in header's public fields. C owns the ABI and C long width here. */
typedef struct {
    void *av_class;
    uint8_t *buffer;
    int buffer_size;
    uint8_t *buf_ptr, *buf_end;
    void *opaque, *read_packet, *write_packet, *seek;
    int64_t pos;
    int eof_reached, error, write_flag, max_packet_size, min_packet_size;
    unsigned long checksum;
    uint8_t *checksum_ptr;
    void *update_checksum, *read_pause, *read_seek;
    int seekable, direct;
    uint8_t *protocol_whitelist, *protocol_blacklist;
    void *write_data_type;
    int ignore_boundary_point;
    uint8_t *buf_ptr_max;
    int64_t bytes_read, bytes_written;
} SmokeAVIOContext;

EXPORT int smoke_sizeof_long(void) { return (int)sizeof(unsigned long); }
EXPORT int smoke_sizeof_avio(void) { return (int)sizeof(SmokeAVIOContext); }
EXPORT int smoke_offset_checksum(void) { return (int)offsetof(SmokeAVIOContext, checksum); }
EXPORT int smoke_offset_checksum_ptr(void) { return (int)offsetof(SmokeAVIOContext, checksum_ptr); }
EXPORT int smoke_offset_bytes_read(void) { return (int)offsetof(SmokeAVIOContext, bytes_read); }
