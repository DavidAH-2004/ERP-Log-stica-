/**
 * ERP desarrollado por Marcelo Zepeda (@Marzza01)
 * Todos los derechos reservados © 2026
 */

using System.Security.Cryptography;
using System.Text;
using MarketplaceERP.API.Security;
using MarketplaceERP.Core.Data;
using MarketplaceERP.Core.Entities;
using MarketplaceERP.Shared.Settings;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace MarketplaceERP.API.Controllers;

/// <summary>
/// Request de login solo por Email.
/// </summary>
public record LoginRequest(string? Email, string? Password, string? Username = null);

/// <summary>
/// Response de login exitoso con soporte de campos duales para frontend.
/// </summary>
public record LoginResponse(string Token, string Role, DateTime ExpiresAt, string Usuario, string Rol);

/// <summary>
/// Controlador de autenticación JWT.
/// POST /api/auth/login → valida contra core.usuarios mediante PasswordHasher y retorna JWT firmado.
/// </summary>
[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly CoreDbContext _coreDb;
    private readonly JwtSettings _jwtSettings;
    private readonly bool _enforceSessionVersion;
    private readonly ILogger<AuthController> _logger;
    private readonly PasswordHasher<Usuario> _passwordHasher;
    private static readonly Usuario _dummyUsuario = new() { Email = "dummy@security.internal" };
    private static readonly string _dummyHash = new PasswordHasher<Usuario>().HashPassword(
        _dummyUsuario, Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)));

    public AuthController(
        CoreDbContext coreDb,
        IOptions<JwtSettings> jwtOptions,
        ILogger<AuthController> logger,
        IConfiguration? configuration = null)
    {
        _coreDb = coreDb;
        _jwtSettings = jwtOptions.Value;
        _enforceSessionVersion = configuration?.GetValue<bool>("Auth:EnableSessionVersionCheck") ?? false;
        _logger = logger;
        _passwordHasher = new PasswordHasher<Usuario>();
    }

    /// <summary>
    /// Autentica un usuario solo por Email normalizado en minúsculas y retorna un JWT Bearer token.
    /// Rate limit aplicado: 5 intentos por minuto por IP (retorna 429).
    /// Prevención de timing attack: si el usuario no existe, se ejecuta VerifyHashedPassword contra un dummy hash.
    /// Registro en logs: se usa email enmascarado para evitar fugar inputs sensibles.
    /// </summary>
    [AllowAnonymous]
    [EnableRateLimiting("LoginLimiter")]
    [HttpPost("login")]
    [ProducesResponseType(typeof(LoginResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> Login([FromBody] LoginRequest request, CancellationToken cancellationToken)
    {
        var rawEmail = !string.IsNullOrWhiteSpace(request.Email)
            ? request.Email
            : request.Username;

        if (string.IsNullOrWhiteSpace(rawEmail) || string.IsNullOrWhiteSpace(request.Password))
            return BadRequest(new { mensaje = "El correo electrónico y la contraseña son obligatorios." });

        var normalizedEmail = rawEmail.Trim().ToLowerInvariant();
        var maskedEmail = MaskEmail(normalizedEmail);

        // Buscar usuario estrictamente por Email normalizado en minúsculas
        var usuario = await _coreDb.Usuarios
            .FirstOrDefaultAsync(u => u.Email == normalizedEmail, cancellationToken);

        if (usuario is null || !usuario.Activo)
        {
            // Ejecutar dummy check para mitigar timing attack (tiempos constantes)
            _passwordHasher.VerifyHashedPassword(_dummyUsuario, _dummyHash, request.Password);
            _logger.LogWarning("Intento de login fallido para cuenta: {MaskedEmail}", maskedEmail);
            return Unauthorized(new { mensaje = "Credenciales inválidas." });
        }

        var verificationResult = _passwordHasher.VerifyHashedPassword(usuario, usuario.PasswordHash, request.Password);
        if (verificationResult == PasswordVerificationResult.Failed)
        {
            _logger.LogWarning("Intento de login fallido para cuenta: {MaskedEmail}", maskedEmail);
            return Unauthorized(new { mensaje = "Credenciales inválidas." });
        }

        // Una cuenta histórica con rol en minúsculas conserva los permisos
        // previstos. Roles desconocidos se rechazan sin emitir JWT.
        var rolCanonico = UsuarioRoles.Canonizar(usuario.Rol);
        if (rolCanonico is null)
        {
            _logger.LogWarning("Login rechazado por rol desconocido. Usuario={UsuarioId}", usuario.Id);
            return Unauthorized(new { mensaje = "Credenciales inválidas." });
        }

        var token = GenerarToken(usuario, rolCanonico);
        var expiresAt = DateTime.UtcNow.AddMinutes(_jwtSettings.ExpirationMinutes);

        _logger.LogInformation("Login exitoso. Usuario={UsuarioId}, Rol={Role}", usuario.Id, rolCanonico);

        return Ok(new LoginResponse(token, rolCanonico, expiresAt, usuario.Nombre, rolCanonico));
    }

    /// <summary>
    /// F353: endpoint de sesión para el BFF HttpOnly de PREVIEW.
    /// El JWT permanece en el servidor proxy; aquí solo se exponen los
    /// datos mínimos de identidad. Se comprueba usuario activo y rol vigente.
    /// No habilita revocación global ni sustituye la pasarela de pagos.
    /// </summary>
    [Authorize(Roles = "Admin,Vendedor,Comprador")]
    [HttpGet("session")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Session(CancellationToken cancellationToken)
    {
        var idValue = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var expiryValue = User.FindFirst(JwtRegisteredClaimNames.Exp)?.Value;
        if (!Guid.TryParse(idValue, out var usuarioId) ||
            !long.TryParse(expiryValue, out var expiryUnix) ||
            expiryUnix <= DateTimeOffset.UtcNow.ToUnixTimeSeconds())
        {
            return Unauthorized(new { mensaje = "Sesión inválida." });
        }

        var usuario = await _coreDb.Usuarios
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == usuarioId && u.Activo, cancellationToken);

        var rolCanonico = usuario is null ? null : UsuarioRoles.Canonizar(usuario.Rol);
        if (usuario is null || rolCanonico is null ||
            !string.Equals(User.FindFirst(ClaimTypes.Role)?.Value, rolCanonico, StringComparison.Ordinal))
        {
            return Unauthorized(new { mensaje = "Sesión inválida." });
        }

        return Ok(new
        {
            usuario = usuario.Nombre,
            rol = rolCanonico,
            role = rolCanonico,
            expiresAt = DateTimeOffset.FromUnixTimeSeconds(expiryUnix).UtcDateTime,
            email = usuario.Email,
            vendedorId = rolCanonico == "Vendedor" ? usuario.VendedorId : null
        });
    }

    /// <summary>
    /// F360: atomically invalidate all previous JWTs for the account.
    /// Disabled until the database migration and explicit cutover gate succeed.
    /// </summary>
    [Authorize(Roles = "Admin,Vendedor,Comprador")]
    [HttpPost("logout")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> Logout(CancellationToken cancellationToken)
    {
        if (!_enforceSessionVersion)
            return StatusCode(StatusCodes.Status503ServiceUnavailable,
                new { mensaje = "Revocación todavía no habilitada." });

        var idText = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var versionText = User.FindFirst("sv")?.Value;
        if (!Guid.TryParse(idText, out var userId) || userId == Guid.Empty ||
            !int.TryParse(versionText, out var version) || version < 0)
            return Unauthorized(new { mensaje = "Sesión inválida." });

        try
        {
            var changed = await _coreDb.Usuarios
                .Where(u => u.Id == userId && u.Activo && u.VersionSesion == version &&
                    u.VersionSesion < int.MaxValue)
                .ExecuteUpdateAsync(setters => setters.SetProperty(
                    u => u.VersionSesion, u => u.VersionSesion + 1), cancellationToken);
            if (changed != 1)
                return Unauthorized(new { mensaje = "La sesión ya fue revocada." });

            _logger.LogInformation("Sesiones anteriores revocadas para Usuario={UsuarioId}.", userId);
            return NoContent();
        }
        catch (Exception ex) when (
            ex is DbUpdateException || ex is InvalidOperationException ||
            ex is System.Data.Common.DbException || ex is OperationCanceledException)
        {
            _logger.LogWarning("No se pudo confirmar la revocación de sesión.");
            return StatusCode(StatusCodes.Status503ServiceUnavailable,
                new { mensaje = "La revocación no se confirmó." });
        }
    }

    private string GenerarToken(Usuario usuario, string rolCanonico)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_jwtSettings.Key));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, usuario.Id.ToString()),
            new(ClaimTypes.NameIdentifier, usuario.Id.ToString()),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new("sv", usuario.VersionSesion.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ClaimValueTypes.Integer32),
            new(ClaimTypes.Name, usuario.Email),
            new(ClaimTypes.Role, rolCanonico),
            new(JwtRegisteredClaimNames.Iat,
                DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(),
                ClaimValueTypes.Integer64)
        };

        if (usuario.VendedorId.HasValue && rolCanonico == "Vendedor")
        {
            claims.Add(new Claim("VendedorId", usuario.VendedorId.Value.ToString()));
        }

        var token = new JwtSecurityToken(
            issuer: _jwtSettings.Issuer,
            audience: _jwtSettings.Audience,
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(_jwtSettings.ExpirationMinutes),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private static string MaskEmail(string email)
    {
        var parts = email.Split('@');
        if (parts.Length != 2 || string.IsNullOrEmpty(parts[0]))
        {
            return "***@***";
        }
        var name = parts[0];
        var domain = parts[1];
        var maskedName = name.Length <= 2 ? name[0] + "***" : name[..2] + new string('*', Math.Max(1, name.Length - 2));
        return $"{maskedName}@{domain}";
    }
}
