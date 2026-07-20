namespace GdbMiBridge.Mcp;

public static class RegisterSets
{
    private static readonly HashSet<string> X64Set = new(StringComparer.OrdinalIgnoreCase)
    {
        "rax", "rbx", "rcx", "rdx", "rsi", "rdi", "rbp", "rsp", "rip",
        "rflags", "cs", "ds", "es", "fs", "gs", "ss",
        "cr0", "cr2", "cr3", "cr4", "eflags"
    };

    private static readonly HashSet<string> X86Set = new(StringComparer.OrdinalIgnoreCase)
    {
        "eax", "ebx", "ecx", "edx", "esi", "edi", "ebp", "esp", "eip",
        "eflags", "cs", "ds", "es", "fs", "gs", "ss",
        "cr0", "cr2", "cr3", "cr4"
    };

    private static readonly HashSet<string> ARM64Set = new(StringComparer.OrdinalIgnoreCase)
    {
        "x0","x1","x2","x3","x4","x5","x6","x7","x8","x9",
        "x10","x11","x12","x13","x14","x15","x16","x17","x18","x19",
        "x20","x21","x22","x23","x24","x25","x26","x27","x28","x29","x30",
        "sp", "pc", "cpsr", "fpcr", "fpsr"
    };

    private static readonly HashSet<string> ARMSet = new(StringComparer.OrdinalIgnoreCase)
    {
        "r0","r1","r2","r3","r4","r5","r6","r7","r8","r9","r10",
        "r11","r12", "sp", "lr", "pc", "cpsr"
    };

    private static readonly HashSet<string> MipsSet = new(StringComparer.OrdinalIgnoreCase)
    {
        "zero", "at", "v0", "v1", "a0", "a1", "a2", "a3",
        "t0","t1","t2","t3","t4","t5","t6","t7",
        "s0","s1","s2","s3","s4","s5","s6","s7",
        "t8","t9", "k0", "k1", "gp", "sp", "fp", "ra",
        "pc", "status", "cause", "epc"
    };

    public static HashSet<string> GetBasicSet(GdbMi.TargetArchitecture arch) => arch switch
    {
        GdbMi.TargetArchitecture.X64 => X64Set,
        GdbMi.TargetArchitecture.X86 => X86Set,
        GdbMi.TargetArchitecture.ARM64 => ARM64Set,
        GdbMi.TargetArchitecture.ARM => ARMSet,
        GdbMi.TargetArchitecture.Mips => MipsSet,
        _ => new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    };
}
