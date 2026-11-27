using Microsoft.AspNetCore.Identity;
using ResursKapacitet.Api.Data;

namespace ResursKapacitet.Api.Services;

/// <summary>Enkel lösenordshantering med PBKDF2 (ASP.NET Core Identitys hasher). Användarna ligger i en tabell.</summary>
public static class Passwords
{
    private static readonly PasswordHasher<User> Hasher = new();

    public static string Hash(User user, string password) => Hasher.HashPassword(user, password);

    public static bool Verify(User user, string password)
    {
        if (string.IsNullOrEmpty(user.PasswordHash)) return false;
        var result = Hasher.VerifyHashedPassword(user, user.PasswordHash, password);
        return result is PasswordVerificationResult.Success or PasswordVerificationResult.SuccessRehashNeeded;
    }
}
