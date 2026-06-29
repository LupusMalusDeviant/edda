using System.Text;
using System.Text.Json;
using Amazon;
using Amazon.BedrockRuntime;
using Amazon.BedrockRuntime.Model;
using Amazon.Runtime;
using Edda.Core.Exceptions;

namespace Edda.AKG.Ingestion.Llm;

/// <summary>
/// <see cref="ILlmChatClient"/> backed by Claude models on AWS Bedrock via the AWS SDK
/// (<c>InvokeModel</c>). Infrastructure adapter (real AWS SDK + SigV4) — deliberately not unit-tested,
/// consistent with the LibGit2Sharp Git adapter (ADR-0002). The request and response bodies mirror the
/// Anthropic Messages API, so <see cref="AnthropicChatClient.ParseContent"/> is reused for parsing.
/// </summary>
public sealed class AwsBedrockChatClient : ILlmChatClient
{
    private const string BedrockAnthropicVersion = "bedrock-2023-05-31";
    private const int DefaultMaxTokens = 4096;

    private readonly string? _accessKeyId;
    private readonly string? _secretAccessKey;
    private readonly string _region;
    private readonly string _model;

    /// <summary>Initializes a new instance of the <see cref="AwsBedrockChatClient"/> class.</summary>
    /// <param name="accessKeyId">AWS access key id; null falls back to the default AWS credential chain.</param>
    /// <param name="secretAccessKey">AWS secret access key; null falls back to the default AWS credential chain.</param>
    /// <param name="region">AWS region (e.g. <c>us-east-1</c>).</param>
    /// <param name="model">Bedrock model id (e.g. <c>anthropic.claude-opus-4-8</c>).</param>
    public AwsBedrockChatClient(string? accessKeyId, string? secretAccessKey, string region, string model)
    {
        _accessKeyId = accessKeyId;
        _secretAccessKey = secretAccessKey;
        _region = region;
        _model = model;
    }

    /// <inheritdoc />
    public string ProviderName => "bedrock";

    /// <inheritdoc />
    public async Task<string> CompleteAsync(
        string systemPrompt,
        string userPrompt,
        CancellationToken cancellationToken = default)
    {
        var regionEndpoint = RegionEndpoint.GetBySystemName(_region);
        using var client = CreateClient(regionEndpoint);

        var body = JsonSerializer.Serialize(new
        {
            anthropic_version = BedrockAnthropicVersion,
            max_tokens = DefaultMaxTokens,
            system = systemPrompt,
            messages = new[] { new { role = "user", content = userPrompt } },
        });

        var request = new InvokeModelRequest
        {
            ModelId = _model,
            ContentType = "application/json",
            Accept = "application/json",
            Body = new MemoryStream(Encoding.UTF8.GetBytes(body)),
        };

        InvokeModelResponse response;
        try
        {
            response = await client.InvokeModelAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (AmazonServiceException ex)
        {
            throw new ProviderException(
                ProviderName, $"Bedrock request failed: {ex.Message}", (int)ex.StatusCode, innerException: ex);
        }
        catch (AmazonClientException ex)
        {
            throw new ProviderException(ProviderName, $"Bedrock request failed: {ex.Message}", innerException: ex);
        }

        using var reader = new StreamReader(response.Body, Encoding.UTF8);
        var json = await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
        return AnthropicChatClient.ParseContent(json);
    }

    /// <summary>
    /// Creates the Bedrock runtime client, using explicit credentials when both are supplied and falling
    /// back to the default AWS credential chain (environment, profile, instance role) otherwise.
    /// </summary>
    private AmazonBedrockRuntimeClient CreateClient(RegionEndpoint regionEndpoint)
    {
        if (!string.IsNullOrWhiteSpace(_accessKeyId) && !string.IsNullOrWhiteSpace(_secretAccessKey))
        {
            return new AmazonBedrockRuntimeClient(
                new BasicAWSCredentials(_accessKeyId, _secretAccessKey), regionEndpoint);
        }

        return new AmazonBedrockRuntimeClient(regionEndpoint);
    }
}
