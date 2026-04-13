namespace FirstMud.DesignTools.Shared;

/// <summary>Tiny CLI flag parser — --flag value or --flag=value.</summary>
public static class Args
{
    public static string? Value(string[] args, string name)
    {
        for (var i = 0; i < args.Length; i++)
        {
            var a = args[i];
            if (a == name && i + 1 < args.Length) return args[i + 1];
            if (a.StartsWith(name + "=", StringComparison.Ordinal))
                return a[(name.Length + 1)..];
        }
        return null;
    }

    public static int? IntValue(string[] args, string name)
    {
        var v = Value(args, name);
        return int.TryParse(v, out var i) ? i : null;
    }

    public static bool Has(string[] args, string name) =>
        args.Any(a => a == name || a.StartsWith(name + "=", StringComparison.Ordinal));
}
