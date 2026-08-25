namespace Brawlhalalla.SelfTest;

/// <summary>A dependency-free test runner — `dotnet run` and read the output.</summary>
public sealed class Harness
{
    private int _passed;
    private readonly List<string> _failures = [];

    public void Test(string name, Action body)
    {
        try
        {
            body();
            _passed++;
            Console.WriteLine($"  PASS  {name}");
        }
        catch (Exception ex)
        {
            _failures.Add($"{name}\n          {ex.Message}");
            Console.WriteLine($"  FAIL  {name}");
            Console.WriteLine($"        {ex.Message}");
        }
    }

    public int Finish()
    {
        Console.WriteLine();
        Console.WriteLine(new string('-', 72));
        if (_failures.Count == 0)
        {
            Console.WriteLine($"  All {_passed} checks passed.");
            return 0;
        }

        Console.WriteLine($"  {_passed} passed, {_failures.Count} FAILED:");
        foreach (string failure in _failures)
            Console.WriteLine($"    - {failure}");
        return 1;
    }

    public static void IsTrue(bool condition, string what)
    {
        if (!condition) throw new AssertionException($"Expected {what}.");
    }

    public static void AreEqual<T>(T expected, T actual, string what)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new AssertionException($"Expected {what} to be <{Show(expected)}> but got <{Show(actual)}>.");
    }

    public static void Contains(string haystack, string needle, string what)
    {
        if (!haystack.Contains(needle, StringComparison.Ordinal))
            throw new AssertionException($"Expected {what}: could not find <{needle}>.");
    }

    public static void DoesNotContain(string haystack, string needle, string what)
    {
        if (haystack.Contains(needle, StringComparison.Ordinal))
            throw new AssertionException($"Expected {what}: unexpectedly found <{needle}>.");
    }

    public static void Throws(Action body, string what)
    {
        try
        {
            body();
        }
        catch
        {
            return;
        }
        throw new AssertionException($"Expected {what} to throw, but it succeeded.");
    }

    private static string Show<T>(T value)
    {
        string s = value?.ToString() ?? "null";
        return s.Length > 120 ? s[..120] + "..." : s;
    }
}

public sealed class AssertionException : Exception
{
    public AssertionException(string message) : base(message) { }
}
