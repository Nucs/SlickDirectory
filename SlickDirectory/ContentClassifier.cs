using System.Text.RegularExpressions;

namespace SlickDirectory;

public static class ContentClassifier
{
    private static readonly Dictionary<string, Regex> _patterns;

    public static string Classify(string text)
    {
        foreach (var pattern in _patterns)
        {
            if (pattern.Value.IsMatch(text))
            {
                return pattern.Key;
            }
        }

        return "txt";
    }

    static ContentClassifier()
    {
        _patterns = new Dictionary<string, Regex>
        {
            { "cs", new Regex(@"(using\s+[\w\.]+;|namespace\s+\w+)", RegexOptions.Compiled) },
            { "json", new Regex(@"^\s*(\{|\[).*(\}|\])\s*$", RegexOptions.Compiled | RegexOptions.Singleline) },
            { "java", new Regex(@"(public\s+class|import\s+java\.|System\.out\.println)", RegexOptions.Compiled) },
            { "py", new Regex(@"(def\s+\w+\(.*\):|import\s+\w+|if\s+__name__\s*==\s*['""]__main__['""])", RegexOptions.Compiled) },
            { "html", new Regex(@"<!DOCTYPE\s+html>|<html>|<body>", RegexOptions.Compiled | RegexOptions.IgnoreCase) },
            { "css", new Regex(@"(\w+\s*\{\s*\w+:|\w+\s*:\s*\w+;)", RegexOptions.Compiled) },
            { "js", new Regex(@"(function\s+\w+\(.*\)|let\s+\w+\s*=|const\s+\w+\s*=|var\s+\w+\s*=)", RegexOptions.Compiled) },
            { "xml", new Regex(@"<\?xml\s+version=|<\w+>\s*<\/\w+>", RegexOptions.Compiled) },
            { "sql", new Regex(@"(SELECT\s+.*\s+FROM|CREATE\s+TABLE|INSERT\s+INTO)", RegexOptions.Compiled | RegexOptions.IgnoreCase) },
            { "url", new Regex(@"^https?:/", RegexOptions.Compiled | RegexOptions.IgnoreCase) }
        };

        var testCases = new Dictionary<string, string>
        {
            { "cs", "using System; class Program { static void Main() { } }" },
            { "json", "{ \"name\": \"John\", \"age\": 30 }" },
            { "java", "public class Main { public static void main(String[] args) { System.out.println(\"Hello, World!\"); } }" },
            { "py", "def hello(): print('Hello, World!')\n\nif __name__ == '__main__':\n    hello()" },
            { "html", "<!DOCTYPE html><html><body><h1>Hello, World!</h1></body></html>" },
            { "css", "body { font-family: Arial; color: #333; }" },
            { "js", "function greet(name) { console.log(`Hello, ${name}!`); }" },
            { "xml", "<?xml version=\"1.0\" encoding=\"UTF-8\"?><root><element>Content</element></root>" },
            { "sql", "SELECT * FROM users WHERE age > 18;" },
            { "url", "https://www.example.com/yooo/?asd=asd" },
            { "txt", "This is just some plain text." }
        };

        foreach (var testCase in testCases)
        {
            string result = Classify(testCase.Value);
            if (result != testCase.Key)
            {
                Console.WriteLine($"Test case for {testCase.Key}: {(result == testCase.Key ? "PASSED" : "FAILED")}");
                Console.WriteLine($"  Expected: {testCase.Key}, Got: {result}");
            }
        }
    }
}