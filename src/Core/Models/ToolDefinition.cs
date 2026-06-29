namespace Edda.Core.Models;

/// <summary>
/// Describes a tool that can be offered to the LLM.
/// Supports both JSON-Schema (Anthropic/OpenAI) and OpenAPI export formats (D-03).
/// </summary>
public sealed record ToolDefinition
{
    /// <summary>snake_case tool name. Must be unique within the ToolRegistry.</summary>
    public required string Name { get; init; }

    /// <summary>Human-readable description used by the LLM to decide when to call this tool.</summary>
    public required string Description { get; init; }

    /// <summary>JSON Schema object describing the tool's input parameters.</summary>
    public required object InputSchema { get; init; }

    /// <summary>
    /// Returns the input schema in JSON Schema format, as required by Anthropic and OpenAI APIs.
    /// </summary>
    /// <returns>The JSON Schema object for this tool's parameters.</returns>
    public object ToJsonSchema() => InputSchema;

    /// <summary>
    /// Returns an OpenAPI operation object for external integration and documentation export.
    /// </summary>
    /// <returns>An anonymous object conforming to the OpenAPI operation structure.</returns>
    public object ToOpenApi() =>
        new
        {
            operationId = Name,
            summary = Description,
            requestBody = new
            {
                required = true,
                content = new
                {
                    applicationJson = new { schema = InputSchema }
                }
            }
        };
}
