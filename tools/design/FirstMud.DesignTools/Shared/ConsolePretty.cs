namespace FirstMud.DesignTools.Shared;

/// <summary>Tiny color-coded console helper for design tool output.</summary>
public static class ConsolePretty
{
    public static void Header(string text)
    {
        WithColor(ConsoleColor.Cyan, () => Console.WriteLine($"=== {text} ==="));
    }

    public static void Info(string text) => Console.WriteLine(text);

    public static void Good(string text) =>
        WithColor(ConsoleColor.Green, () => Console.WriteLine($"  ok  {text}"));

    public static void Warn(string text) =>
        WithColor(ConsoleColor.Yellow, () => Console.WriteLine($"  warn  {text}"));

    public static void Error(string text) =>
        WithColor(ConsoleColor.Red, () => Console.Error.WriteLine($"  ERROR  {text}"));

    public static void Beat(string text) =>
        WithColor(ConsoleColor.Magenta, () => Console.WriteLine($"  > {text}"));

    public static void Choice(string text) =>
        WithColor(ConsoleColor.DarkYellow, () => Console.WriteLine($"    [choice] {text}"));

    public static void Delta(string text) =>
        WithColor(ConsoleColor.DarkCyan, () => Console.WriteLine($"    {text}"));

    private static void WithColor(ConsoleColor color, Action write)
    {
        var prev = Console.ForegroundColor;
        try
        {
            Console.ForegroundColor = color;
            write();
        }
        finally
        {
            Console.ForegroundColor = prev;
        }
    }
}
