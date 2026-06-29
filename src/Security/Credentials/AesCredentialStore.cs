using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Edda.Core.Abstractions;
using Edda.Core.Exceptions;
using Microsoft.Extensions.Logging;

namespace Edda.Security.Credentials;

/// <summary>
/// AES-256-GCM encrypted credential store backed by a single encrypted file on disk.
/// The encryption key is stored at <c>data/.credential-key</c> and is created on first use.
/// All operations are serialized via a <see cref="SemaphoreSlim"/> to prevent data corruption.
/// </summary>
public sealed class AesCredentialStore : ICredentialStore
{
    private const string KeyFilePath = "data/.credential-key";
    private const string DataFilePath = "data/credentials.enc";

    private readonly IFileSystem _fileSystem;
    private readonly ILogger<AesCredentialStore> _logger;
    private readonly SemaphoreSlim _lock = new(1, 1);

    /// <summary>
    /// Initializes a new instance of <see cref="AesCredentialStore"/>.
    /// </summary>
    /// <param name="fileSystem">Abstracted filesystem for all I/O.</param>
    /// <param name="timeProvider">Time provider (reserved for future use, e.g. key rotation).</param>
    /// <param name="logger">Logger for operational diagnostics.</param>
    public AesCredentialStore(
        IFileSystem fileSystem,
        TimeProvider timeProvider,
        ILogger<AesCredentialStore> logger)
    {
        _fileSystem = fileSystem;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task StoreAsync(
        string key,
        string value,
        CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var encKey = await GetOrCreateKeyAsync(cancellationToken).ConfigureAwait(false);
            var dict = await ReadAllAsync(encKey, cancellationToken).ConfigureAwait(false);
            dict[key] = value;
            await WriteAllAsync(encKey, dict, cancellationToken).ConfigureAwait(false);
            _logger.LogDebug("Credential stored for key prefix '{Prefix}'", GetKeyPrefix(key));
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <inheritdoc />
    public async Task<string?> RetrieveAsync(
        string key,
        CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var encKey = await GetOrCreateKeyAsync(cancellationToken).ConfigureAwait(false);
            var dict = await ReadAllAsync(encKey, cancellationToken).ConfigureAwait(false);
            return dict.TryGetValue(key, out var value) ? value : null;
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> ListAsync(
        CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var encKey = await GetOrCreateKeyAsync(cancellationToken).ConfigureAwait(false);
            var dict = await ReadAllAsync(encKey, cancellationToken).ConfigureAwait(false);
            return dict.Keys.ToList().AsReadOnly();
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <inheritdoc />
    public async Task DeleteAsync(
        string key,
        CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var encKey = await GetOrCreateKeyAsync(cancellationToken).ConfigureAwait(false);
            var dict = await ReadAllAsync(encKey, cancellationToken).ConfigureAwait(false);
            if (dict.Remove(key))
            {
                await WriteAllAsync(encKey, dict, cancellationToken).ConfigureAwait(false);
                _logger.LogDebug("Credential deleted for key prefix '{Prefix}'", GetKeyPrefix(key));
            }
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// Returns the first segment of a scoped key for log output (everything before the first colon).
    /// Avoids logging full key names that could contain sensitive user identifiers.
    /// </summary>
    private static string GetKeyPrefix(string key)
    {
        var colonIndex = key.IndexOf(':', StringComparison.Ordinal);
        return colonIndex > 0 ? key[..colonIndex] : key;
    }

    /// <summary>
    /// Retrieves the 32-byte AES key from disk, or generates and persists a new one if absent.
    /// </summary>
    private async Task<byte[]> GetOrCreateKeyAsync(CancellationToken ct)
    {
        if (_fileSystem.FileExists(KeyFilePath))
        {
            return await _fileSystem.ReadAllBytesAsync(KeyFilePath, ct).ConfigureAwait(false);
        }

        var newKey = RandomNumberGenerator.GetBytes(32);
        _fileSystem.EnsureDirectoryExists("data");
        await _fileSystem.WriteAllBytesAsync(KeyFilePath, newKey, ct).ConfigureAwait(false);
        return newKey;
    }

    /// <summary>
    /// Reads and decrypts the credential dictionary from disk.
    /// Returns an empty dictionary if no data file exists yet.
    /// </summary>
    /// <exception cref="CredentialDecryptionException">
    /// Thrown if the data file exists but cannot be decrypted with the current key.
    /// </exception>
    private async Task<Dictionary<string, string>> ReadAllAsync(byte[] key, CancellationToken ct)
    {
        if (!_fileSystem.FileExists(DataFilePath))
        {
            return [];
        }

        var data = await _fileSystem.ReadAllBytesAsync(DataFilePath, ct).ConfigureAwait(false);
        var json = Decrypt(key, data);
        return JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? [];
    }

    /// <summary>
    /// Serializes and encrypts the credential dictionary, then writes it to disk.
    /// </summary>
    private async Task WriteAllAsync(byte[] key, Dictionary<string, string> dict, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(dict);
        var encrypted = Encrypt(key, json);
        _fileSystem.EnsureDirectoryExists("data");
        await _fileSystem.WriteAllBytesAsync(DataFilePath, encrypted, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Encrypts a plaintext string using AES-256-GCM.
    /// The output format is: nonce (12 bytes) + tag (16 bytes) + ciphertext.
    /// </summary>
    /// <param name="key">32-byte AES key.</param>
    /// <param name="plaintext">UTF-8 plaintext to encrypt.</param>
    /// <returns>Concatenated nonce, authentication tag, and ciphertext bytes.</returns>
    private static byte[] Encrypt(byte[] key, string plaintext)
    {
        var plaintextBytes = Encoding.UTF8.GetBytes(plaintext);
        var nonce = RandomNumberGenerator.GetBytes(12);
        var tag = new byte[16];
        var ciphertext = new byte[plaintextBytes.Length];

        using var aes = new AesGcm(key, 16);
        aes.Encrypt(nonce, plaintextBytes, ciphertext, tag);

        var result = new byte[nonce.Length + tag.Length + ciphertext.Length];
        nonce.CopyTo(result, 0);
        tag.CopyTo(result, nonce.Length);
        ciphertext.CopyTo(result, nonce.Length + tag.Length);
        return result;
    }

    /// <summary>
    /// Decrypts AES-256-GCM encrypted data.
    /// Expects the input format: nonce (12 bytes) + tag (16 bytes) + ciphertext.
    /// </summary>
    /// <param name="key">32-byte AES key.</param>
    /// <param name="data">Encrypted byte array with nonce, tag, and ciphertext.</param>
    /// <returns>Decrypted UTF-8 plaintext string.</returns>
    /// <exception cref="CredentialDecryptionException">
    /// Thrown when authentication fails, indicating tampering or a wrong key.
    /// </exception>
    private static string Decrypt(byte[] key, byte[] data)
    {
        try
        {
            var nonce = data[..12];
            var tag = data[12..28];
            var ciphertext = data[28..];
            var plaintext = new byte[ciphertext.Length];

            using var aes = new AesGcm(key, 16);
            aes.Decrypt(nonce, ciphertext, tag, plaintext);
            return Encoding.UTF8.GetString(plaintext);
        }
        catch (Exception ex)
        {
            throw new CredentialDecryptionException(ex);
        }
    }
}
