#include <stdint.h>
#include <errno.h>
#include <sys/resource.h>
#include <time.h>
struct route_cpu_stamp { double user_seconds, system_seconds; uint64_t process_ns, thread_ns; };
int route_cpu_read(struct route_cpu_stamp *out) {
    struct rusage usage;
    if (getrusage(RUSAGE_SELF, &usage) != 0) return errno;
    out->user_seconds = usage.ru_utime.tv_sec + usage.ru_utime.tv_usec / 1000000.0;
    out->system_seconds = usage.ru_stime.tv_sec + usage.ru_stime.tv_usec / 1000000.0;
    out->process_ns = clock_gettime_nsec_np(CLOCK_PROCESS_CPUTIME_ID);
    out->thread_ns = clock_gettime_nsec_np(CLOCK_THREAD_CPUTIME_ID);
    return 0;
}
int route_cpu_stamp_size(void) { return sizeof(struct route_cpu_stamp); }
