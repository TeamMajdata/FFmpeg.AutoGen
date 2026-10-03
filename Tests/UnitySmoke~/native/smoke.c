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
EXPORT size_t av_cpu_max_align(void) { return (size_t)0xf1234567U; }
EXPORT int smoke_outstanding_allocations(void) { return outstanding_allocations; }

EXPORT char *av_strdup(const char *s)
{
    if (!s) return NULL;
    size_t n = strlen(s) + 1;
    char *p = (char *)tracked_alloc(n);
    if (p) memcpy(p, s, n);
    return p;
}

EXPORT int av_file_map(const char *filename, uint8_t **data, size_t *size, int log_offset, void *log_context)
{
    if (!filename || !data || !size || log_offset != 0x2468 || log_context != (void *)(uintptr_t)0x3456)
        return -22;
    *data = (uint8_t *)av_strdup("mapped");
    *size = 6;
    return *data ? 0 : -12;
}

EXPORT void av_file_unmap(uint8_t *data, size_t size)
{
    if (size == 6) av_free(data);
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

typedef BufferRef *(*pool_alloc_callback)(size_t);
typedef struct { size_t size; pool_alloc_callback allocate; } BufferPool;

EXPORT BufferPool *av_buffer_pool_init(size_t size, pool_alloc_callback allocate)
{
    BufferPool *pool = (BufferPool *)tracked_alloc(sizeof(BufferPool));
    if (pool) { pool->size = size; pool->allocate = allocate; }
    return pool;
}

EXPORT BufferRef *av_buffer_pool_get(BufferPool *pool)
{
    return pool && pool->allocate ? pool->allocate(pool->size) : NULL;
}

EXPORT void av_buffer_pool_uninit(BufferPool **pool)
{
    if (pool && *pool) { av_free(*pool); *pool = NULL; }
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

EXPORT void av_image_copy_plane_uc_from(uint8_t *dst, ptrdiff_t dst_stride, const uint8_t *src,
                                        ptrdiff_t src_stride, ptrdiff_t byte_width, int height)
{
    for (int row = 0; row < height; ++row)
        memcpy(dst + row * dst_stride, src + row * src_stride, (size_t)byte_width);
}

EXPORT void av_image_copy_uc_from(uint8_t *dst[4], const ptrdiff_t dst_stride[4],
                                  const uint8_t *src[4], const ptrdiff_t src_stride[4],
                                  int pixel_format, int width, int height)
{
    (void)pixel_format;
    for (int plane = 0; plane < 2; ++plane)
        av_image_copy_plane_uc_from(dst[plane], dst_stride[plane], src[plane], src_stride[plane], width, height);
}

typedef struct { int num, den; } Rational;
EXPORT Rational av_add_q(Rational a, Rational b)
{
    Rational result = { a.num * b.den + b.num * a.den, a.den * b.den };
    return result;
}

EXPORT int64_t av_rescale_q(int64_t value, Rational source, Rational destination)
{
    return value * source.num * destination.den / source.den / destination.num;
}

EXPORT double av_display_rotation_get(const int32_t matrix[9]) { return matrix[0] + matrix[8]; }
EXPORT void av_display_rotation_set(int32_t matrix[9], double angle)
{
    matrix[0] = (int32_t)angle;
    matrix[8] = -(int32_t)angle;
}
