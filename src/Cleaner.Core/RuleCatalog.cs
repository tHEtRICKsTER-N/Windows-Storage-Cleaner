using System.Text.Json;

namespace Cleaner.Core;

public static class RuleCatalog
{
    public static CleaningRule[] Load()
    {
        using var stream = typeof(RuleCatalog).Assembly.GetManifestResourceStream("Cleaner.Core.rules.json")
            ?? throw new InvalidOperationException("The bundled rules are missing.");
        var rules = JsonSerializer.Deserialize<CleaningRule[]>(stream, ReportJson.Options)
            ?? throw new InvalidOperationException("The bundled rules are invalid.");
        Validate(rules);
        return rules;
    }

    public static void Validate(IReadOnlyList<CleaningRule> rules)
    {
        if (rules.Count == 0 || rules.Count > 100 || rules.Select(x => x.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count() != rules.Count)
            throw new ArgumentException("Rules must have unique IDs and a bounded count.");
        foreach (var rule in rules)
        {
            if (string.IsNullOrWhiteSpace(rule.Id) || string.IsNullOrWhiteSpace(rule.Title) ||
                string.IsNullOrWhiteSpace(rule.Explanation) || rule.Version < 1 ||
                rule.MinimumAgeDays < 1 || rule.MinimumAgeDays > 3650 ||
                rule.Patterns is not { Length: > 0 and <= 20 } ||
                rule.Patterns.Any(p => string.IsNullOrWhiteSpace(p) || p.IndexOfAny(['\\', '/', ':']) >= 0) ||
                rule.Risk is not ("Review" or "Rebuildable"))
                throw new ArgumentException($"Invalid rule: {rule.Id}");
            if (rule.Category is not ("System caches" or "Graphics" or "Browsers" or "Development" or "Diagnostics"))
                throw new ArgumentException($"Invalid rule category: {rule.Id}");
            var path = rule.RelativePath;
            if (string.IsNullOrWhiteSpace(path) || Path.IsPathRooted(path) ||
                path.IndexOfAny([':', '*', '?', '%']) >= 0 ||
                path.Split(['\\', '/']).Any(x => string.IsNullOrWhiteSpace(x) || x is "." or ".." || x.EndsWith('.') || x.EndsWith(' ')))
                throw new ArgumentException($"Unsafe rule root: {rule.Id}");
        }
    }
}
