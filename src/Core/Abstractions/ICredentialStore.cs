namespace Edda.Core.Abstractions;

/// <summary>
/// Encrypted credential storage using AES-256-GCM.
/// The encryption key is stored in .credential-key and must never be committed to source control.
/// All keys should be user-scoped (include UserId in the key name).
/// </summary>
public interface ICredentialStore
{
    /// <summary>
    /// Stores a secret under the given key. Overwrites any existing value.
    /// </summary>
    /// <param name="key">The storage key (should be user-scoped, e.g. "{userId}:api-key-openai").</param>
    /// <param name="value">The plaintext secret to encrypt and store.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task StoreAsync(
        string key,
        string value,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves and decrypts a stored secret.
    /// </summary>
    /// <param name="key">The storage key.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The decrypted secret, or null if the key does not exist.</returns>
    Task<string?> RetrieveAsync(
        string key,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists all stored keys without decrypting their values.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>All keys currently stored in the credential store.</returns>
    Task<IReadOnlyList<string>> ListAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a stored secret. No-op if the key does not exist.
    /// </summary>
    /// <param name="key">The storage key to delete.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task DeleteAsync(
        string key,
        CancellationToken cancellationToken = default);
}
