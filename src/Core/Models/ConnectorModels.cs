using System.Collections.ObjectModel;

namespace Edda.Core.Models;

/// <summary>
/// A persisted, user-configured instance of a knowledge connector (e.g. one Git repository to scrape).
/// Holds only non-secret field values; secret fields (tokens) live in the credential store under
/// <c>{userId}:source:{Id}:{fieldKey}</c>, never in <see cref="Values"/>.
/// </summary>
public sealed record ConnectorInstanceConfig
{
    /// <summary>Stable, unique id of this source instance. Used to scope its secrets in the credential store.</summary>
    public required string Id { get; init; }

    /// <summary>The connector type this instance is for (matches <see cref="ConnectorDescriptor.TypeId"/>).</summary>
    public required string TypeId { get; init; }

    /// <summary>Human-readable name shown in the UI.</summary>
    public required string Name { get; init; }

    /// <summary>Non-secret field values keyed by <see cref="ConnectorField.Key"/>.</summary>
    public IReadOnlyDictionary<string, string> Values { get; init; } =
        ReadOnlyDictionary<string, string>.Empty;
}

/// <summary>
/// Declarative description of a connector type: its identity plus the input fields the UI renders to
/// configure an instance. This is what makes new source types pluggable without bespoke UI (see ADR-0005).
/// </summary>
public sealed record ConnectorDescriptor
{
    /// <summary>Stable type discriminator (e.g. "git", "custom-http").</summary>
    public required string TypeId { get; init; }

    /// <summary>Human-readable type name shown in the UI.</summary>
    public required string DisplayName { get; init; }

    /// <summary>Optional description shown alongside the type.</summary>
    public string? Description { get; init; }

    /// <summary>The fields the UI renders to configure an instance of this connector.</summary>
    public IReadOnlyList<ConnectorField> Fields { get; init; } = [];
}

/// <summary>A single declarative input field of a <see cref="ConnectorDescriptor"/>.</summary>
public sealed record ConnectorField
{
    /// <summary>Stable field key used in <see cref="ConnectorInstanceConfig.Values"/> and for secret scoping.</summary>
    public required string Key { get; init; }

    /// <summary>Human-readable label shown in the UI.</summary>
    public required string Label { get; init; }

    /// <summary>The field's input type, used by the UI to render the right control.</summary>
    public ConnectorFieldType Type { get; init; } = ConnectorFieldType.Text;

    /// <summary>Whether the field must be provided.</summary>
    public bool Required { get; init; }

    /// <summary>Optional default value.</summary>
    public string? Default { get; init; }

    /// <summary>Optional help text shown beneath the field.</summary>
    public string? Help { get; init; }
}

/// <summary>The input type of a <see cref="ConnectorField"/>, guiding UI rendering and storage.</summary>
public enum ConnectorFieldType
{
    /// <summary>Single-line text.</summary>
    Text,

    /// <summary>Secret value — masked in the UI and stored encrypted in the credential store, not in settings.</summary>
    Secret,

    /// <summary>A URL (single-line text with URL semantics).</summary>
    Url,

    /// <summary>A boolean toggle (stored as "true"/"false").</summary>
    Boolean,

    /// <summary>A list of lines (e.g. glob patterns), one entry per line.</summary>
    TextList,
}
