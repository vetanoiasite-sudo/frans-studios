using System.Security.Cryptography;
using FransStudios.Api.Configuration;
using FransStudios.Api.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace FransStudios.Api.Authentication;

/// <summary>
/// Wraps ASP.NET Core Identity's <see cref="PasswordHasher{TUser}"/> (PBKDF2-HMAC-SHA512, salted,
/// versioned format with automatic re-hash) without pulling in the rest of ASP.NET Core Identity.
/// </summary>
public sealed class PasswordService(IOptions<AuthOptions> options)
{
    private readonly PasswordHasher<User> _hasher = new();
    // Used to spend the same CPU time when the username doesn't exist (prevents user enumeration by timing).
    private static readonly string DummyHash = new PasswordHasher<User>().HashPassword(new User(), Guid.NewGuid().ToString());

    // No look-alike characters (0/O, 1/l/I) so passwords can be read out over the phone.
    private const string Alphabet = "abcdefghijkmnpqrstuvwxyzABCDEFGHJKLMNPQRSTUVWXYZ23456789";

    public string Hash(User user, string password) => _hasher.HashPassword(user, password);

    /// <returns>(valid, needsRehash)</returns>
    public (bool Valid, bool NeedsRehash) Verify(User? user, string password)
    {
        if (user is null)
        {
            _hasher.VerifyHashedPassword(new User(), DummyHash, password);
            return (false, false);
        }
        var result = _hasher.VerifyHashedPassword(user, user.PasswordHash, password);
        return (result != PasswordVerificationResult.Failed, result == PasswordVerificationResult.SuccessRehashNeeded);
    }

    public string? Validate(string? password)
    {
        var min = options.Value.MinPasswordLength;
        if (string.IsNullOrEmpty(password) || password.Length < min)
            return $"Password must be at least {min} characters.";
        if (password.Length > 128) return "Password is too long.";
        return null;
    }

    /// <summary>A 16-character random password, e.g. for new client accounts.</summary>
    public static string Generate(int length = 16) =>
        string.Create(length, 0, (span, _) =>
        {
            for (var i = 0; i < span.Length; i++) span[i] = Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)];
        });
}
