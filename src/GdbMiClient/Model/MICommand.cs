namespace GdbMi;

public record MICommand(string Name, string Arguments)
{
    public string MiText =>
        string.IsNullOrEmpty(Arguments) ? Name : $"{Name} {Arguments}";
}
