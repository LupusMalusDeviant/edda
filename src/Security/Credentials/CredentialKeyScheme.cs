namespace Edda.Security.Credentials;

/// <summary>
/// Builds and validates user-scoped keys for <see cref="Edda.Core.Abstractions.ICredentialStore"/>.
/// Keys follow the convention <c>{userId}:{name}</c>. The name is limited to a predictable,
/// injection-safe character set so keys stay stable and free of separators that would break scoping.
/// </summary>
public static class CredentialKeyScheme
{
    /// <summary>
    /// The maximum allowed length of a credential name.
    /// </summary>
    public const int MaxNameLength = 128;

    /// <summary>
    /// Returns true if the credential name is non-empty, within <see cref="MaxNameLength"/>, and
    /// contains only lowercase letters, digits, and the characters '-', '_', '.' and ':'.
    /// </summary>
    /// <param name="name">The credential name to validate.</param>
    /// <returns>True if the name is valid; otherwise false.</returns>
    public static bool IsValidName(string? name)
    {
        if (string.IsNullOrEmpty(name) || name.Length > MaxNameLength)
        {
            return false;
        }

        foreach (var c in name)
        {
            var isAllowed = c is (>= 'a' and <= 'z') or (>= '0' and <= '9') or '-' or '_' or '.' or ':';
            if (!isAllowed)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Builds the storage key for a credential by scoping <paramref name="name"/> to
    /// <paramref name="userId"/>.
    /// </summary>
    /// <param name="userId">The owning user id (must be non-empty).</param>
    /// <param name="name">The credential name (must satisfy <see cref="IsValidName"/>).</param>
    /// <returns>The scoped storage key in the form <c>{userId}:{name}</c>.</returns>
    /// <exception cref="ArgumentException">Thrown if the user id is empty or the name is invalid.</exception>
    public static string Scope(string userId, string name)
    {
        if (string.IsNullOrEmpty(userId))
        {
            throw new ArgumentException("User id must not be empty.", nameof(userId));
        }

        if (!IsValidName(name))
        {
            throw new ArgumentException($"Invalid credential name: '{name}'.", nameof(name));
        }

        return $"{userId}:{name}";
    }
}
