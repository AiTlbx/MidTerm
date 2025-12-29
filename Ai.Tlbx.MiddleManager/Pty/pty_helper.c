// Native helper for macOS PTY setup
// Compile: clang -O2 pty_helper.c -o pty_helper
// Usage: pty_helper /dev/ttysXXX /bin/zsh -l

#include <unistd.h>
#include <fcntl.h>
#include <stdlib.h>

int main(int argc, char *argv[])
{
    if (argc < 3) return 1;

    const char *slave = argv[1];

    // Become session leader (required for controlling terminal)
    setsid();

    // Open PTY slave - this sets it as controlling terminal
    int fd = open(slave, O_RDWR);
    if (fd < 0) return 2;

    // Set up stdin/stdout/stderr
    dup2(fd, 0);
    dup2(fd, 1);
    dup2(fd, 2);
    if (fd > 2) close(fd);

    // Exec the shell
    execvp(argv[2], &argv[2]);
    return 3;
}
