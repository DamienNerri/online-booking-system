using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using OnlineBooking.Api.Common;
using OnlineBooking.Api.Configuration;
using OnlineBooking.Api.Models;
using OnlineBooking.Api.Repositories;

namespace OnlineBooking.Api.Services;

/// <summary>Inscription, connexion et émission de JWT. Mots de passe hachés via BCrypt (Req 8.4).</summary>
public sealed class AuthService
{
    private readonly UserRepository _users;
    private readonly JwtOptions _jwt;

    public AuthService(UserRepository users, IOptions<JwtOptions> jwt)
    {
        _users = users;
        _jwt = jwt.Value;
    }

    public async Task<AuthResponse> RegisterAsync(RegisterRequest req, CancellationToken ct = default)
    {
        ValidateCredentials(req.Email, req.Password);

        var hash = BCrypt.Net.BCrypt.HashPassword(req.Password);
        var user = await _users.CreateAsync(req.Email.Trim().ToLowerInvariant(), hash, "CUSTOMER", ct);
        if (user is null)
        {
            throw new ConflictException("Un compte existe déjà pour cet email.");
        }
        return BuildResponse(user);
    }

    public async Task<AuthResponse> LoginAsync(LoginRequest req, CancellationToken ct = default)
    {
        ValidateCredentials(req.Email, req.Password);

        var user = await _users.FindByEmailAsync(req.Email.Trim().ToLowerInvariant(), ct);
        if (user is null || !BCrypt.Net.BCrypt.Verify(req.Password, user.PasswordHash))
        {
            // Message neutre : ne pas révéler si l'email existe (Req 9 sécurité).
            throw new UnauthorizedException("Email ou mot de passe invalide.");
        }
        return BuildResponse(user);
    }

    private static void ValidateCredentials(string? email, string? password)
    {
        if (string.IsNullOrWhiteSpace(email) || !email.Contains('@'))
        {
            throw new ValidationException("Email invalide.");
        }
        if (string.IsNullOrWhiteSpace(password) || password.Length < 8)
        {
            throw new ValidationException("Le mot de passe doit faire au moins 8 caractères.");
        }
    }

    private AuthResponse BuildResponse(User user)
    {
        var token = GenerateToken(user);
        return new AuthResponse(token, user.Email, user.Role);
    }

    private string GenerateToken(User user)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_jwt.Secret));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new Claim(JwtRegisteredClaimNames.Email, user.Email),
            new Claim(ClaimTypes.Role, user.Role),
        };

        var token = new JwtSecurityToken(
            issuer: _jwt.Issuer,
            audience: _jwt.Issuer,
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(_jwt.ExpiresMinutes),
            signingCredentials: creds);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
