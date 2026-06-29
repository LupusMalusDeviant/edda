namespace Edda.Core.Exceptions;

/// <summary>
/// Base exception for credential store errors.
/// </summary>
public class CredentialException : EddaException
{
    /// <summary>Initializes a new CredentialException.</summary>
    public CredentialException(string message, Exception? innerException = null)
        : base("CredentialStore", message, innerException: innerException) { }
}

/// <summary>
/// Thrown when a requested credential key does not exist in the store.
/// </summary>
public sealed class CredentialNotFoundException : CredentialException
{
    /// <summary>The key that was not found.</summary>
    public string Key { get; }

    /// <summary>Initializes a new CredentialNotFoundException.</summary>
    public CredentialNotFoundException(string key)
        : base($"Credential key '{key}' not found.")
    {
        Key = key;
    }
}

/// <summary>
/// Thrown when stored credentials cannot be decrypted, typically indicating
/// a tampered or missing encryption key.
/// </summary>
public sealed class CredentialDecryptionException : CredentialException
{
    /// <summary>Initializes a new CredentialDecryptionException.</summary>
    public CredentialDecryptionException(Exception? innerException = null)
        : base("Failed to decrypt credentials. The encryption key may be corrupted or missing.",
               innerException) { }
}
