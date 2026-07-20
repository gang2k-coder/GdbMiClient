using GdbMiBridge.Mcp;

namespace GdbMiBridge.Mcp.Tests;

public class RegisterSetsTests
{
    [Fact]
    public void GetBasicSet_X64_ReturnsCommonRegisters()
    {
        var set = RegisterSets.GetBasicSet(GdbMi.TargetArchitecture.X64);

        // GPRs
        Assert.Contains("rax", set);
        Assert.Contains("rbx", set);
        Assert.Contains("rcx", set);
        Assert.Contains("rdx", set);
        Assert.Contains("rsi", set);
        Assert.Contains("rdi", set);
        Assert.Contains("rbp", set);
        Assert.Contains("rsp", set);
        Assert.Contains("rip", set);

        // Flags
        Assert.Contains("rflags", set);

        // Segment registers
        Assert.Contains("cs", set);
        Assert.Contains("ss", set);

        // Control registers
        Assert.Contains("cr0", set);
        Assert.Contains("cr2", set);
        Assert.Contains("cr3", set);

        // Should NOT include SIMD registers
        Assert.DoesNotContain("xmm0", set);
        Assert.DoesNotContain("ymm0", set);
        Assert.DoesNotContain("zmm0", set);
    }

    [Fact]
    public void GetBasicSet_X86_Returns32BitRegisters()
    {
        var set = RegisterSets.GetBasicSet(GdbMi.TargetArchitecture.X86);

        Assert.Contains("eax", set);
        Assert.Contains("ebx", set);
        Assert.Contains("ecx", set);
        Assert.Contains("edx", set);
        Assert.Contains("esi", set);
        Assert.Contains("edi", set);
        Assert.Contains("ebp", set);
        Assert.Contains("esp", set);
        Assert.Contains("eip", set);
        Assert.Contains("eflags", set);

        // Should NOT contain x64-specific names
        Assert.DoesNotContain("rax", set);
    }

    [Fact]
    public void GetBasicSet_ARM64_ReturnsCommonRegisters()
    {
        var set = RegisterSets.GetBasicSet(GdbMi.TargetArchitecture.ARM64);

        Assert.Contains("x0", set);
        Assert.Contains("x1", set);
        Assert.Contains("x29", set);
        Assert.Contains("x30", set);
        Assert.Contains("sp", set);
        Assert.Contains("pc", set);
        Assert.Contains("cpsr", set);

        // Should NOT include SIMD/FP regs
        Assert.DoesNotContain("v0", set);
        Assert.DoesNotContain("q0", set);
    }

    [Fact]
    public void GetBasicSet_ARM_Returns32BitRegisters()
    {
        var set = RegisterSets.GetBasicSet(GdbMi.TargetArchitecture.ARM);

        Assert.Contains("r0", set);
        Assert.Contains("r7", set);
        Assert.Contains("sp", set);
        Assert.Contains("lr", set);
        Assert.Contains("pc", set);
        Assert.Contains("cpsr", set);
    }

    [Fact]
    public void GetBasicSet_Mips_ReturnsCommonRegisters()
    {
        var set = RegisterSets.GetBasicSet(GdbMi.TargetArchitecture.Mips);

        Assert.Contains("zero", set);
        Assert.Contains("v0", set);
        Assert.Contains("a0", set);
        Assert.Contains("t0", set);
        Assert.Contains("sp", set);
        Assert.Contains("ra", set);
        Assert.Contains("pc", set);
    }

    [Fact]
    public void GetBasicSet_Unknown_ReturnsEmpty()
    {
        var set = RegisterSets.GetBasicSet(GdbMi.TargetArchitecture.Unknown);
        Assert.Empty(set);
    }

    [Fact]
    public void GetBasicSet_IsCaseInsensitive()
    {
        var set = RegisterSets.GetBasicSet(GdbMi.TargetArchitecture.X64);
        Assert.Contains("RAX", set);
        Assert.Contains("Rax", set);
        Assert.Contains("rax", set);
    }
}
