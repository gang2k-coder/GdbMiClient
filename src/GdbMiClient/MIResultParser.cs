using System.Globalization;
using System.Text;
using Microsoft.Extensions.Logging;

namespace GdbMiClient;

/// <summary>
/// Parses GDB MI output into typed result objects.
/// Ported from MICore/MIResults.cs — all parsing logic preserved.
/// </summary>
public class MIResultParser
{
    private readonly ILogger? _logger;

    public MIResultParser(ILogger? logger = null) { _logger = logger; }

    public Results ParseCommandOutput(string output)
    {
        string resultString = output.Trim();
        int comma = resultString.IndexOf(',');
        if (comma < 0)
            return new Results(ParseResultClass(resultString), new List<NamedResultValue>());

        var resultClass = ParseResultClass(resultString.Substring(0, comma));
        var wholeString = new Span(resultString);
        return ParseResultList(resultString, wholeString.AdvanceTo(comma + 1), resultClass);
    }

    public Results ParseResultList(string listStr, ResultClass resultClass = ResultClass.None)
    {
        string resultString = listStr.Trim();
        return ParseResultList(resultString, new Span(resultString), resultClass);
    }

    public string ParseCString(string input)
    {
        if (string.IsNullOrEmpty(input)) return string.Empty;
        string cstr = input.Trim();
        if (cstr.Length == 0 || cstr[0] != '\"') return input;
        var s = ParseCString(cstr, new Span(cstr), out _);
        return s?.AsString ?? string.Empty;
    }

    internal static ResultClass ParseResultClass(string resultClass) => resultClass switch
    {
        "done" => ResultClass.Done,
        "running" => ResultClass.Running,
        "connected" => ResultClass.Connected,
        "error" => ResultClass.Error,
        "exit" => ResultClass.Exit,
        _ => ResultClass.None
    };

    private struct Span
    {
        private static readonly Span _emptySpan = new(0, 0);
        public int Start { get; }
        public int Length { get; }
        public int Extent => Start + Length;
        public bool IsEmpty => Length == 0;
        public static Span Empty => _emptySpan;

        public Span(string s) { Start = 0; Length = s.Length; }
        public Span(int start, int len) { Start = start; Length = len; }

        public Span Advance(int len) => new(Start + len, Length - len);
        public Span AdvanceTo(int pos) => new(pos, Length - (pos - Start));
        public Span Prefix(int len) => new(Start, len);
        public string Extract(string theString) => theString.Substring(Start, Length);
        public int IndexOf(string theString, char c)
        {
            int i = theString.IndexOf(c, Start);
            return (i < 0 || i >= Extent) ? -1 : i - Start;
        }
        public bool StartsWith(string theString, string pattern)
        {
            if (Length < pattern.Length) return false;
            for (int i = 0; i < pattern.Length; i++)
                if (theString[Start + i] != pattern[i]) return false;
            return true;
        }
    }

    private static bool IsValueChar(char c) => c is '\"' or '{' or '[';

    private Results ParseResultList(string resultString, Span listStr, ResultClass resultClass)
        => new(resultClass, ParseResultList(resultString,
            (Span s, ref int i) => true,
            (Span s, ref int i) => i == s.Extent,
            listStr, out _) ?? new List<NamedResultValue>());

    private ResultValue? ParseValue(string resultString, Span resultStr, out Span rest)
    {
        rest = Span.Empty;
        if (resultStr.IsEmpty) return null;
        return resultString[resultStr.Start] switch
        {
            '\"' => ParseCString(resultString, resultStr, out rest),
            '{' => ParseTuple(resultString, resultStr, out rest),
            '[' => ParseList(resultString, resultStr, out rest),
            _ => null
        };
    }

    private ResultValue? ParseResultValue(string resultString, Span resultStr, out Span rest)
    {
        rest = Span.Empty;
        if (resultStr.IsEmpty) return null;
        return resultString[resultStr.Start] switch
        {
            '\"' => ParseCString(resultString, resultStr, out rest),
            '{' => ParseResultTuple(resultString, resultStr, out rest),
            '[' => ParseList(resultString, resultStr, out rest),
            _ => null
        };
    }

    private NamedResultValue? ParseResult(string resultString, Span resultStr, out Span rest)
    {
        rest = Span.Empty;
        int equals = resultStr.IndexOf(resultString, '=');
        if (equals < 1) return null;
        string name = resultStr.Prefix(equals).Extract(resultString);
        var value = ParseResultValue(resultString, resultStr.Advance(equals + 1), out rest);
        return value is null ? null : new NamedResultValue(name, value);
    }

    private ConstValue? ParseCString(string resultString, Span input, out Span rest)
    {
        rest = input;
        var output = new StringBuilder();
        if (input.IsEmpty || resultString[input.Start] != '\"') return null;
        int i = input.Start + 1;
        bool endFound = false;
        for (; i < input.Extent; i++)
        {
            char c = resultString[i];
            if (c == '\"')
            {
                i++;
                if (i < input.Extent && resultString[i] == c) { /* escaped quote */ }
                else { endFound = true; break; }
            }
            else if (c == '\\')
            {
                c = resultString[++i];
                c = c switch { 'n' => '\n', 'r' => '\r', 't' => '\t', _ => c };
            }
            output.Append(c);
        }
        if (!endFound) return null;
        rest = input.AdvanceTo(i);
        return new ConstValue(output.ToString());
    }

    private delegate bool EdgeCondition(Span s, ref int i);

    private List<NamedResultValue>? ParseResultList(string resultString,
        EdgeCondition begin, EdgeCondition end, Span input, out Span rest)
    {
        rest = Span.Empty;
        var list = new List<NamedResultValue>();
        int i = input.Start;
        if (!begin(input, ref i)) return null;
        if (end(input, ref i)) { rest = input.AdvanceTo(i); return list; }
        input = input.AdvanceTo(i);
        var item = ParseResult(resultString, input, out rest);
        if (item is null) return null;
        list.Add(item);
        input = rest;
        while (!input.IsEmpty && resultString[input.Start] == ',')
        {
            item = ParseResult(resultString, input.Advance(1), out rest);
            if (item is null) return null;
            list.Add(item);
            input = rest;
        }
        i = input.Start;
        if (!end(input, ref i)) { rest = Span.Empty; return null; }
        rest = input.AdvanceTo(i);
        return list;
    }

    private List<NamedResultValue>? ParseResultList(string resultString,
        char begin, char end, Span input, out Span rest)
        => ParseResultList(resultString,
            (Span s, ref int i) => { if (resultString[i] == begin) { i++; return true; } return false; },
            (Span s, ref int i) => { if (i < s.Extent && resultString[i] == end) { i++; return true; } return false; },
            input, out rest);

    private ResultValue? ParseResultTuple(string resultString, Span input, out Span rest)
    {
        var list = ParseResultList(resultString, '{', '}', input, out rest);
        if (list is null) return null;
        var tlist = new List<ResultValue>();
        while (rest.StartsWith(resultString, ",{"))
        {
            tlist.Add(new TupleValue(list));
            list = ParseResultList(resultString, '{', '}', rest.Advance(1), out rest);
            if (list is null) return null;
        }
        var v = new TupleValue(list);
        if (tlist.Count != 0) { tlist.Add(v); return new ValueListValue(tlist); }
        return v;
    }

    private TupleValue? ParseTuple(string resultString, Span input, out Span rest)
        => ParseResultList(resultString, '{', '}', input, out rest) is { } l ? new TupleValue(l) : null;

    private ResultValue? ParseList(string resultString, Span input, out Span rest)
    {
        rest = Span.Empty;
        if (resultString[input.Start] != '[') return null;
        if (resultString[input.Start + 1] == ']') { rest = input.Advance(2); return new ValueListValue(new()); }
        return IsValueChar(resultString[input.Start + 1])
            ? ParseValueList(resultString, input, out rest)
            : (ResultValue?)ParseResultList(resultString, input, out rest);
    }

    private ValueListValue? ParseValueList(string resultString, Span input, out Span rest)
    {
        rest = Span.Empty;
        if (resultString[input.Start] != '[') return null;
        var list = new List<ResultValue>();
        input = input.Advance(1);
        var item = ParseValue(resultString, input, out rest);
        if (item is null) return null;
        list.Add(item);
        input = rest;
        while (!input.IsEmpty && resultString[input.Start] == ',')
        {
            item = ParseValue(resultString, input.Advance(1), out rest);
            if (item is null) return null;
            list.Add(item);
            input = rest;
        }
        if (input.IsEmpty || resultString[input.Start] != ']') { rest = Span.Empty; return null; }
        rest = input.Advance(1);
        return new ValueListValue(list);
    }

    private ResultListValue? ParseResultList(string resultString, Span input, out Span rest)
        => ParseResultList(resultString, '[', ']', input, out rest) is { } l ? new ResultListValue(l) : null;
}

// ═══════════════════════════════════════════════
// Public result value types — ported from MICore/MIResults.cs
// ═══════════════════════════════════════════════

public class ResultValue
{
    public virtual ResultValue Find(string name)
        => throw new MIResultFormatException(name, this);

    public virtual bool TryFind(string name, out ResultValue? result)
    {
        if (Contains(name)) { result = Find(name); } else { result = null; }
        return result is not null;
    }

    public virtual bool Contains(string name) => false;

    public ConstValue FindConst(string name) => Find<ConstValue>(name);

    public int FindInt(string name)
    {
        try { return FindConst(name).ToInt; }
        catch (MIResultFormatException) { throw; }
        catch (Exception e) { throw new MIResultFormatException(name, this, e); }
    }

    public uint FindUint(string name)
    {
        try { return FindConst(name).ToUint; }
        catch (MIResultFormatException) { throw; }
        catch (Exception e) { throw new MIResultFormatException(name, this, e); }
    }

    public uint? TryFindUint(string name)
    {
        if (!TryFind(name, out ConstValue? c)) return null;
        try { return c.ToUint; } catch (OverflowException) { return null; }
        catch (MIResultFormatException) { throw; }
        catch (Exception e) { throw new MIResultFormatException(name, this, e); }
    }

    public ulong FindAddr(string name)
    {
        try { return FindConst(name).ToAddr; }
        catch (MIResultFormatException) { throw; }
        catch (Exception e) { throw new MIResultFormatException(name, this, e); }
    }

    public ulong? TryFindAddr(string name)
    {
        if (!TryFind(name, out ConstValue? c)) return null;
        try { return c.ToAddr; }
        catch (MIResultFormatException) { throw; }
        catch (Exception e) { throw new MIResultFormatException(name, this, e); }
    }

    public string FindString(string name) => FindConst(name).AsString;
    public string TryFindString(string name)
        => TryFind(name, out ConstValue? c) ? c.AsString : string.Empty;

    public T Find<T>(string name) where T : ResultValue
        => Find(name) is T t ? t : throw new MIResultFormatException(name, this);

    public bool TryFind<T>(string name, out T? result) where T : ResultValue
    {
        if (Contains(name)) result = Find(name) as T; else result = null;
        return result is not null;
    }

    public T? TryFind<T>(string name) where T : ResultValue
    {
        TryFind(name, out T? result);
        return result;
    }
}

public class ConstValue : ResultValue
{
    public readonly string Content;
    public ConstValue(string str) { Content = str ?? string.Empty; }
    public ulong ToAddr => ParseAddr(Content, throwOnError: true);
    public int ToInt => int.Parse(Content, CultureInfo.InvariantCulture);
    public uint ToUint => ParseUint(Content, throwOnError: true);
    public string AsString => Content;
    public override string ToString() => Content;

    internal static ulong ParseAddr(string addr, bool throwOnError = false)
    {
        if (string.IsNullOrEmpty(addr))
        {
            if (throwOnError) throw new ArgumentNullException(nameof(addr));
            return 0;
        }
        if (addr.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            return ulong.Parse(addr.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        return ulong.Parse(addr, NumberStyles.Integer, CultureInfo.InvariantCulture);
    }

    internal static uint ParseUint(string str, bool throwOnError = false)
    {
        if (string.IsNullOrEmpty(str))
        {
            if (throwOnError) throw new ArgumentException(null, nameof(str));
            return 0;
        }
        if (str.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            return uint.Parse(str.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        return uint.Parse(str, NumberStyles.Integer, CultureInfo.InvariantCulture);
    }
}

public class NamedResultValue
{
    public string Name { get; }
    public ResultValue Value { get; }
    public NamedResultValue(string name, ResultValue value) { Name = name; Value = value; }
    public override string ToString() => $"{Name}={Value}";
}

public class TupleValue : ResultValue
{
    public List<NamedResultValue> Content { get; }
    public TupleValue(List<NamedResultValue> list) { Content = list; }

    public override ResultValue Find(string name)
    {
        var item = Content.Find(c => c.Name == name);
        return item is null ? throw new MIResultFormatException(name, this) : item.Value;
    }

    public override bool Contains(string name) => Content.Exists(c => c.Name == name);
    public ResultValue[] FindAll(string name) => Content.FindAll(c => c.Name == name).Select(c => c.Value).ToArray();
    public T[] FindAll<T>(string name) where T : class => FindAll(name).OfType<T>().ToArray();
}

public abstract class ListValue : ResultValue
{
    public abstract int Length { get; }
    public bool IsEmpty() => Length == 0;
}

public class ValueListValue : ListValue
{
    public ResultValue[] Content { get; }
    public override int Length => Content.Length;
    public ValueListValue(List<ResultValue> list) { Content = list.ToArray(); }
    public T[] AsArray<T>() where T : ResultValue => Content.Cast<T>().ToArray();
    public string[] AsStrings => Content.Cast<ConstValue>().Select(c => c.AsString).ToArray();
}

public class ResultListValue : ListValue
{
    public NamedResultValue[] Content { get; }
    public override int Length => Content.Length;
    public ResultListValue(List<NamedResultValue> list) { Content = list.ToArray(); }

    public override ResultValue Find(string name)
    {
        var item = Array.Find(Content, c => c.Name == name);
        return item is null ? throw new MIResultFormatException(name, this) : item.Value;
    }
    public override bool Contains(string name) => Array.Exists(Content, c => c.Name == name);
    public ResultValue[] FindAll(string name) => Array.FindAll(Content, c => c.Name == name).Select(c => c.Value).ToArray();
    public T[] FindAll<T>(string name) where T : class => FindAll(name).OfType<T>().ToArray();
    public string[] FindAllStrings(string name) => FindAll<ConstValue>(name).Select(c => c.AsString).ToArray();
}

public class Results : ResultListValue
{
    public readonly ResultClass ResultClass;
    public Results(ResultClass resultClass, List<NamedResultValue>? list = null)
        : base(list ?? new List<NamedResultValue>()) { ResultClass = resultClass; }

    public Results Add(string name, ResultValue value)
    {
        var l = Content.ToList();
        l.Add(new NamedResultValue(name, value));
        return new Results(ResultClass, l);
    }
}

public class MIResultFormatException : Exception
{
    public MIResultFormatException(string name, ResultValue container, Exception? inner = null)
        : base($"Failed to find '{name}' in result value.", inner) { }
}
