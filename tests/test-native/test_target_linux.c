// Linux native test target for GdbMiBridge.Mcp E2E tests.
// Compile: gcc -g -O0 -o test_target_linux test_target_linux.c
//
// Exported symbols: add, multiply, factorial, loop_body, after_loop, g_counter

#include <unistd.h>
#include <stdlib.h>

int g_counter = 0;

int add(int a, int b)
{
    g_counter++;
    return a + b;
}

int multiply(int a, int b)
{
    g_counter++;
    return a * b;
}

int factorial(int n)
{
    g_counter++;
    if (n <= 1)
        return 1;
    return n * factorial(n - 1);
}

void loop_body(int i)
{
    g_counter++;
}

void after_loop(void)
{
    // intentional no-op — breakpoint target
}

int main(void)
{
    int sum = add(3, 4);
    int prod = multiply(5, 6);
    int fact = factorial(6);

    for (int i = 0; i < 10; i++)
    {
        usleep(100000);  // 100ms
        loop_body(i);
    }

    after_loop();
    return 0;
}
