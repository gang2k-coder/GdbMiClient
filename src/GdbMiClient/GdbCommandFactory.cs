using System.Globalization;
using System.Text;

namespace GdbMi;

/// <summary>
/// Constructs MI command strings for GDB.
/// Ported from MICore: MICommandFactory.cs + gdb.cs.
/// </summary>
public class GdbCommandFactory
{
    private readonly GdbMiClient _client;

    public GdbCommandFactory(GdbMiClient client) { _client = client; }

    // ═══════════ Static helpers (testable) ═══════════

    public static string EscapeQuotes(string str) => str.Replace("\"", "\\\"");
    public static string HandleInvalidChars(string str) => str.Replace("\r", " ").Replace("\n", " ");

    public static bool PreparePath(string path, bool useUnixFormat, out string pathMI)
    {
        bool requiresQuotes = false;
        path = path.Trim();
        if (useUnixFormat) path = path.Replace('\\', '/');
        if (path.IndexOf(' ') != -1) { path = path.Replace("\\", "\\\\"); requiresQuotes = true; }
        pathMI = path;
        return requiresQuotes;
    }

    // ═══════════ Breakpoints ═══════════

    public async Task<Results> BreakInsert(string filename, uint line,
        string? condition = null, bool enabled = true, bool useUnixFormat = true)
    {
        var cmd = new StringBuilder("-break-insert -f ");
        if (condition is not null) { cmd.Append("-c \""); cmd.Append(EscapeQuotes(condition)); cmd.Append("\" "); }
        if (!enabled) cmd.Append("-d ");
        bool quotes = PreparePath(filename, useUnixFormat, out var filenameMI);
        if (quotes) cmd.Append('\"');
        cmd.Append(filenameMI);
        cmd.Append(':');
        cmd.Append(line.ToString(CultureInfo.InvariantCulture));
        if (quotes) cmd.Append('\"');
        return await _client.ExecuteAsync(new MICommand(cmd.ToString(), ""));
    }

    public async Task<Results> BreakInsert(string functionName)
        => await _client.ExecuteAsync(new MICommand("-break-insert", $"-f {functionName}"));

    public async Task<Results> BreakDelete(string bkptno)
        => await _client.ExecuteAsync(new MICommand("-break-delete", bkptno));

    public async Task BreakEnable(string bkptno, bool enabled)
        => await _client.ExecuteAsync(new MICommand(enabled ? "-break-enable" : "-break-disable", bkptno));

    public async Task BreakCondition(string bkptno, string expr)
        => await _client.ExecuteAsync(new MICommand("-break-condition", $"{bkptno} {expr}"));

    public async Task<Results> BreakInfo(string bkptno)
        => await _client.ExecuteAsync(new MICommand("-break-info", bkptno));

    // ═══════════ Execution ═══════════

    public async Task ExecRun()
        => await _client.ExecuteAsync(new MICommand("-exec-run", ""));

    public async Task ExecContinue()
        => await _client.ExecuteAsync(new MICommand("-exec-continue", ""));

    public async Task ExecNext(int threadId)
        => await _client.ExecuteAsync(new MICommand(FmtThread("-exec-next", "", threadId), ""));

    public async Task ExecStep(int threadId)
        => await _client.ExecuteAsync(new MICommand(FmtThread("-exec-step", "", threadId), ""));

    public async Task ExecFinish(int threadId)
        => await _client.ExecuteAsync(new MICommand(FmtThread("-exec-finish", "", threadId), ""));

    public async Task ExecStepInstruction(int threadId)
        => await _client.ExecuteAsync(new MICommand(FmtThread("-exec-step-instruction", "", threadId), ""));

    // ═══════════ Stack ═══════════

    public async Task<TupleValue[]> StackListFrames(int threadId, uint lowFrame = 0, uint highFrame = 1000)
    {
        var args = $"{lowFrame} {highFrame}";
        var r = await _client.ExecuteAsync(new MICommand(FmtThread("-stack-list-frames", args, threadId), ""));
        var list = r.Find<ListValue>("stack");
        return list is ResultListValue rv ? rv.FindAll<TupleValue>("frame") : Array.Empty<TupleValue>();
    }

    public async Task<Results> StackInfoFrame()
        => await _client.ExecuteAsync(new MICommand("-stack-info-frame", ""));

    public async Task<ResultValue> StackListLocals(int printValues, int threadId, uint frameLevel)
        => (await _client.ExecuteAsync(new MICommand(
            FmtThreadFrame("-stack-list-locals", printValues.ToString(), threadId, frameLevel), ""))).Find("locals");

    public async Task<ValueListValue> StackListVariables(int printValues, int threadId, uint frameLevel)
        => (await _client.ExecuteAsync(new MICommand(
            FmtThreadFrame("-stack-list-variables", printValues.ToString(), threadId, frameLevel), "")))
            .Find<ValueListValue>("variables");

    public async Task<Results> StackInfoDepth(int maxDepth = 1000)
        => await _client.ExecuteAsync(new MICommand("-stack-info-depth", maxDepth.ToString()));

    // ═══════════ Data ═══════════

    public async Task<string> DataEvaluateExpression(string expr, int threadId, uint frameLevel)
        => (await _client.ExecuteAsync(new MICommand(
            FmtThreadFrame("-data-evaluate-expression", $"\"{EscapeQuotes(expr)}\"", threadId, frameLevel), "")))
            .FindString("value");

    public async Task<string[]> DataListRegisterNames()
        => (await _client.ExecuteAsync(new MICommand("-data-list-register-names", "")))
            .Find<ValueListValue>("register-names").AsStrings;

    public async Task<TupleValue[]> DataListRegisterValues(int threadId)
        => (await _client.ExecuteAsync(new MICommand(FmtThread("-data-list-register-values", "x", threadId), "")))
            .Find<ValueListValue>("register-values").AsArray<TupleValue>();

    // ═══════════ Variable objects ═══════════

    public async Task<Results> VarCreate(string expression, int threadId, uint frameLevel)
    {
        string escaped = EscapeQuotes(HandleInvalidChars(expression));
        return await _client.ExecuteAsync(new MICommand(
            FmtThreadFrame("-var-create", $"- * \"{escaped}\"", threadId, frameLevel), ""));
    }

    public async Task<Results> VarListChildren(string variableName)
        => await _client.ExecuteAsync(new MICommand("-var-list-children",
            $"--simple-values \"{variableName}\" 0 1000"));

    public async Task<Results> VarEvaluateExpression(string variableName)
        => await _client.ExecuteAsync(new MICommand("-var-evaluate-expression", variableName));

    public async Task<string> VarAssign(string variableName, string expression, int threadId, uint frameLevel)
    {
        string escaped = EscapeQuotes(HandleInvalidChars(expression));
        return (await _client.ExecuteAsync(new MICommand(
            FmtThreadFrame("-var-assign", $"{variableName} \"{escaped}\"", threadId, frameLevel), "")))
            .FindString("value");
    }

    public async Task VarDelete(string variableName)
        => await _client.ExecuteAsync(new MICommand("-var-delete", variableName));

    // ═══════════ Thread / Misc ═══════════

    public async Task<Results> ThreadInfo(uint? threadId = null)
        => await _client.ExecuteAsync(new MICommand("-thread-info",
            threadId.HasValue ? threadId.Value.ToString() : ""));

    public async Task<Results> SetOption(string variable, string value)
        => await _client.ExecuteAsync(new MICommand("-gdb-set", $"{variable} {value}"));

    public async Task FileExecAndSymbols(string programPath)
        => await _client.ExecuteAsync(new MICommand("-file-exec-and-symbols", $"\"{EscapeQuotes(programPath)}\""));

    public async Task EnableTargetAsyncOption()
    {
        var r = await _client.ExecuteAsync(new MICommand("-gdb-set", "mi-async on"));
        if (r.ResultClass == ResultClass.Error)
            await _client.ExecuteAsync(new MICommand("-gdb-set", "target-async on"));
    }

    public async Task<HashSet<string>> GetFeatures()
        => new((await _client.ExecuteAsync(new MICommand("-list-features", "")))
            .Find<ValueListValue>("features").AsStrings);

    public async Task Terminate()
        => await _client.ExecuteAsync(new MICommand("kill", ""));

    public async Task<TargetArchitecture> GetTargetArchitecture()
    {
        var result = await _client.ConsoleCmdAsync("show architecture", allowWhileRunning: false);
        using var reader = new StringReader(result);
        while (reader.ReadLine() is string line)
        {
            if (line.Contains("x86-64", StringComparison.OrdinalIgnoreCase)) return TargetArchitecture.X64;
            if (line.Contains("i386", StringComparison.OrdinalIgnoreCase)) return TargetArchitecture.X86;
            if (line.Contains("arm64", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("aarch64", StringComparison.OrdinalIgnoreCase)) return TargetArchitecture.ARM64;
            if (line.Contains("arm", StringComparison.OrdinalIgnoreCase)) return TargetArchitecture.ARM;
        }
        return TargetArchitecture.Unknown;
    }

    // ═══════════ Internal ═══════════

    private static string FmtThreadFrame(string cmd, string args, int threadId, uint frameLevel)
        => $"{cmd} --thread {threadId} --frame {frameLevel} {args}".TrimEnd();

    private static string FmtThread(string cmd, string args, int threadId)
        => $"{cmd} --thread {threadId} {args}".TrimEnd();
}
