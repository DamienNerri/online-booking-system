using System.Security.Cryptography;
using System.Text;
using OnlineBooking.Api.Common;
using OnlineBooking.Api.Models;
using OnlineBooking.Api.Repositories;

namespace OnlineBooking.Api.Services;

/// <summary>
/// Service MFA TOTP (RFC 6238) — implémentation sans dépendance externe.
/// Génère un secret, produit une URL otpauth:// compatible Google Authenticator,
/// et vérifie un code à 6 chiffres avec fenêtre ±1 intervalle (30 s).
/// </summary>
public sealed class MfaService
{
    private readonly UserRepository _users;

    public MfaService(UserRepository users) => _users = users;

    /// <summary>Génère un secret TOTP et retourne l'URL otpauth pour scanner dans un authenticator.</summary>
    public async Task<MfaSetupResponse> SetupAsync(long userId, string userEmail, CancellationToken ct = default)
    {
        // 20 octets = 160 bits — taille recommandée RFC 4226
        var secretBytes = RandomNumberGenerator.GetBytes(20);
        var secretBase64 = Convert.ToBase64String(secretBytes);
        var secretBase32 = ToBase32(secretBytes);

        await _users.SetMfaSecretAsync(userId, secretBase64, ct);

        // URL compatible Google Authenticator, Authy, etc.
        var otpUrl = $"otpauth://totp/HotelBooking:{Uri.EscapeDataString(userEmail)}" +
                     $"?secret={secretBase32}&issuer=HotelBooking&algorithm=SHA1&digits=6&period=30";

        return new MfaSetupResponse(secretBase32, otpUrl);
    }

    /// <summary>Active la MFA après vérification du premier code TOTP.</summary>
    public async Task<bool> ActivateAsync(long userId, string code, CancellationToken ct = default)
    {
        var secret = await _users.GetMfaSecretAsync(userId, ct)
            ?? throw new ValidationException("MFA non configurée. Appelez /api/auth/mfa/setup d'abord.");

        if (!VerifyCode(secret, code))
            return false;

        await _users.EnableMfaAsync(userId, ct);
        return true;
    }

    /// <summary>Vérifie un code TOTP. Appelé à chaque login quand mfa_enabled = true.</summary>
    public async Task<bool> VerifyAsync(long userId, string code, CancellationToken ct = default)
    {
        var secret = await _users.GetMfaSecretAsync(userId, ct);
        return secret is not null && VerifyCode(secret, code);
    }

    // --- TOTP RFC 6238 (HMAC-SHA1, pas de dépendance externe) ---

    private static bool VerifyCode(string secretBase64, string code)
    {
        if (code.Length != 6 || !code.All(char.IsDigit)) return false;
        var secretBytes = Convert.FromBase64String(secretBase64);
        var counter = DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 30;

        // Fenêtre ±1 intervalle pour compenser les décalages d'horloge (~30 s)
        for (var i = -1; i <= 1; i++)
        {
            if (ComputeTotp(secretBytes, counter + i) == code)
                return true;
        }
        return false;
    }

    private static string ComputeTotp(byte[] secret, long counter)
    {
        var counterBytes = BitConverter.GetBytes(counter);
        if (BitConverter.IsLittleEndian) Array.Reverse(counterBytes); // TOTP = big-endian

        using var hmac = new HMACSHA1(secret);
        var hash = hmac.ComputeHash(counterBytes);

        // Truncation dynamique RFC 4226 §5.3
        var offset = hash[^1] & 0x0F;
        var code = ((hash[offset]     & 0x7F) << 24)
                 | ((hash[offset + 1] & 0xFF) << 16)
                 | ((hash[offset + 2] & 0xFF) << 8)
                 |  (hash[offset + 3] & 0xFF);

        return (code % 1_000_000).ToString("D6");
    }

    private static string ToBase32(byte[] bytes)
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        var sb = new StringBuilder();
        var buffer = 0;
        var bitsLeft = 0;
        foreach (var b in bytes)
        {
            buffer = (buffer << 8) | b;
            bitsLeft += 8;
            while (bitsLeft >= 5)
            {
                bitsLeft -= 5;
                sb.Append(alphabet[(buffer >> bitsLeft) & 31]);
            }
        }
        if (bitsLeft > 0) sb.Append(alphabet[(buffer << (5 - bitsLeft)) & 31]);
        return sb.ToString();
    }
}
