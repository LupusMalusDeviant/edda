using Edda.Core.Models;

namespace Edda.AKG.Context;

/// <summary>
/// Resolves which toolbox sub-domains are relevant for a given task context.
/// Uses keyword matching against the user's task text and extracted concepts
/// to determine which tool categories should be loaded into the AKG context.
/// </summary>
internal sealed class ToolboxResolver
{
    /// <summary>
    /// Maps each toolbox domain name to its trigger keywords.
    /// A toolbox is considered relevant if any of its keywords appear
    /// in the task text or extracted concepts.
    /// </summary>
    internal static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> ToolboxKeywords =
        new Dictionary<string, IReadOnlyList<string>>
        {
            ["tools.browser"] =
            [
                "browser", "navigate", "click", "fill", "screenshot", "page", "html",
                "dom", "element", "webpage", "webseite", "seite", "formular", "eingabe",
                "button", "link", "url", "browsen", "automatisierung",
            ],
            ["tools.code"] =
            [
                "python", "code", "script", "execute", "ausführen", "shell", "bash",
                "terminal", "command", "befehl", "programmieren", "interpreter",
                "pip", "run", "compile", "debug",
            ],
            ["tools.custom"] =
            [
                "custom tool", "eigenes tool", "plugin", "erstellen", "installieren",
                "benutzerdefiniert", "erweiterung", "addon",
            ],
            ["tools.devops"] =
            [
                "docker", "container", "image", "deploy", "deployment", "devops",
                "orchestrierung", "service", "infrastruktur", "build",
            ],
            ["tools.files"] =
            [
                "datei", "file", "upload", "download", "senden", "send", "anhang",
                "attachment", "verzeichnis", "ordner", "directory", "pfad", "path",
            ],
            ["tools.knowledge"] =
            [
                "wissen", "knowledge", "akg", "regel", "rule", "kontext", "context",
                "kompilieren", "compile", "analyse", "codebase", "repository", "repo",
            ],
            ["tools.memory"] =
            [
                "merken", "remember", "erinnern", "recall", "vergessen", "forget",
                "gedächtnis", "memory", "speichern", "store", "nutzer", "user",
                "userdata", "learning", "lernen",
            ],
            ["tools.multiagent"] =
            [
                "recherche", "research", "parallel", "agent", "hand", "hands",
                "autonom", "autonomous", "workflow", "skill", "multi-agent",
                "delegieren", "delegate", "dag",
            ],
            ["tools.scheduling"] =
            [
                "trigger", "schedule", "zeitplan", "timer", "cron", "intervall",
                "benachrichtigung", "notification", "alarm", "erinnerung", "reminder",
                "task queue", "warteschlange",
            ],
            ["tools.security"] =
            [
                "credential", "passwort", "password", "schlüssel", "key", "api-key",
                "token", "secret", "geheimnis", "zugang", "access",
            ],
            ["tools.web"] =
            [
                "suche", "search", "web", "internet", "fetch", "http", "api",
                "request", "url", "website", "online", "googl", "bing",
                "herunterladen", "abrufen", "abfragen",
            ],
        };

    /// <summary>
    /// Resolves which toolbox domains are relevant for the given task context.
    /// Always includes "custom-tools" to ensure user-created tools are considered.
    /// Returns all toolbox domains (graceful degradation) when no keywords match.
    /// </summary>
    /// <param name="context">The current task context with user message and concepts.</param>
    /// <returns>Set of relevant toolbox domain names.</returns>
    internal IReadOnlySet<string> Resolve(TaskContext context)
    {
        var result = new HashSet<string>(StringComparer.Ordinal) { "custom-tools" };

        var textLower = context.Task.ToLowerInvariant();
        var conceptsLower = context.Concepts
            .Select(c => c.ToLowerInvariant())
            .ToHashSet(StringComparer.Ordinal);

        foreach (var (domain, keywords) in ToolboxKeywords)
        {
            foreach (var keyword in keywords)
            {
                if (textLower.Contains(keyword, StringComparison.Ordinal)
                    || conceptsLower.Any(c => c.Contains(keyword, StringComparison.Ordinal)))
                {
                    result.Add(domain);
                    break;
                }
            }
        }

        // Fallback: if no toolbox matched, load all (graceful degradation)
        if (result.Count == 1) // only custom-tools
        {
            foreach (var domain in ToolboxKeywords.Keys)
                result.Add(domain);
        }

        return result;
    }
}
