namespace Edda.Core.Models;

/// <summary>
/// Discriminates the type of event written to the audit log.
/// All values are append-only — existing values must never be renamed or removed.
/// </summary>
public enum AuditEvent
{
    /// <summary>User input was modified by the InputSanitizer.</summary>
    InputSanitized,

    /// <summary>A model completion was requested.</summary>
    ModelCall,

    /// <summary>A tool was executed.</summary>
    ToolExecute,

    /// <summary>A tool execution failed.</summary>
    ToolError,

    /// <summary>A TDK validator detected a rule violation.</summary>
    TdkViolation,

    /// <summary>A credential was accessed via ICredentialStore.</summary>
    CredentialAccess,

    /// <summary>A clone container was spawned.</summary>
    CloneSpawned,

    /// <summary>A clone container was stopped or cleaned up.</summary>
    CloneStopped,

    /// <summary>The agent configuration was changed at runtime.</summary>
    ConfigChanged,

    /// <summary>A user proposed a new AKG rule.</summary>
    RuleProposed,

    /// <summary>An AKG rule was deleted.</summary>
    RuleDeleted,

    /// <summary>A taint-sink check blocked a tool call due to a data-flow violation.</summary>
    TaintViolation,

    /// <summary>A taint label was explicitly removed via ITaintTracker.Declassify().</summary>
    TaintDeclassify,

    /// <summary>A plugin was installed from the curated registry.</summary>
    PluginInstalled,

    /// <summary>A plugin was removed via the registry or manually.</summary>
    PluginRemoved,

    /// <summary>A plugin was developed by the self-development pipeline.</summary>
    PluginDeveloped,

    /// <summary>An authentication attempt (success or failure) via API key or other mechanism.</summary>
    AuthenticationAttempt
}
