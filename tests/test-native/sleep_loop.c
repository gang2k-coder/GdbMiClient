// Linux sleep loop helper for attach/detach E2E tests.
// Compile: gcc -g -O0 -o sleep_loop sleep_loop.c
// Outputs "PID=<pid>" on startup so the test harness can attach.

#include <stdio.h>
#include <unistd.h>

void loop_func(int n)
{
    (void)n;
    // just a named function so the test can verify we're inside it
}

int main(void)
{
    printf("PID=%d\n", getpid());
    fflush(stdout);

    for (int i = 0; i < 100; i++)
    {
        loop_func(i);
        usleep(200000);  // 200ms
    }
    return 0;
}
