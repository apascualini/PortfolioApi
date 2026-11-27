using System.ComponentModel.DataAnnotations;
using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Google.Apis.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using PortfolioApi.Data;
using PortfolioApi.Models;

namespace PortfolioApi.Controllers;

public record GoogleLoginRequest([Required] string IdToken);
public record SendOtpRequest([Required, EmailAddress] string Email, string? FullName);
public record VerifyOtpRequest([Required, EmailAddress] string Email, [Required, StringLength(6, MinimumLength = 6)] string Code);

[ApiController]
[Route("api/customer-auth")]
public class CustomerAuthController : ControllerBase
{
    private readonly AppDbContext _context;
    private readonly IConfiguration _config;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<CustomerAuthController> _logger;

    public CustomerAuthController(
        AppDbContext context,
        IConfiguration config,
        IHttpClientFactory httpClientFactory,
        ILogger<CustomerAuthController> logger)
    {
        _context = context;
        _config = config;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    // POST: api/customer-auth/google
    [HttpPost("google")]
    public async Task<IActionResult> LoginWithGoogle(GoogleLoginRequest request)
    {
        var googleClientId = _config["Google:ClientId"]
            ?? throw new InvalidOperationException("Google:ClientId is missing.");

        GoogleJsonWebSignature.Payload payload;
        try
        {
            var settings = new GoogleJsonWebSignature.ValidationSettings
            {
                Audience = new[] { googleClientId }
            };
            payload = await GoogleJsonWebSignature.ValidateAsync(request.IdToken, settings);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Invalid Google ID Token.");
            return Unauthorized(new { message = "Token de Google inválido o expirado." });
        }

        var email = payload.Email.Trim().ToLowerInvariant();
        var customer = await _context.Customers.FirstOrDefaultAsync(c => c.Email == email);

        if (customer is null)
        {
            customer = new Customer
            {
                Email = email,
                FullName = payload.Name ?? email.Split('@')[0],
                GoogleSubjectId = payload.Subject,
                IsEmailVerified = true
            };
            _context.Customers.Add(customer);
        }
        else
        {
            customer.GoogleSubjectId ??= payload.Subject;
            customer.IsEmailVerified = true;
            if (string.IsNullOrWhiteSpace(customer.FullName) && !string.IsNullOrWhiteSpace(payload.Name))
                customer.FullName = payload.Name;
        }

        await _context.SaveChangesAsync();
        SetCustomerCookie(customer);

        return Ok(new { id = customer.Id, email = customer.Email, fullName = customer.FullName });
    }

    // POST: api/customer-auth/send-otp
    [HttpPost("send-otp")]
    public async Task<IActionResult> SendOtp(SendOtpRequest request)
    {
        var email = request.Email.Trim().ToLowerInvariant();
        var code = RandomNumberGenerator.GetInt32(100000, 999999).ToString();

        var customer = await _context.Customers.FirstOrDefaultAsync(c => c.Email == email);
        if (customer is null)
        {
            customer = new Customer
            {
                Email = email,
                FullName = string.IsNullOrWhiteSpace(request.FullName) ? email.Split("@")[0] : request.FullName.Trim(),
                IsEmailVerified = false
            };
            _context.Customers.Add(customer);
        }
        else if (!string.IsNullOrWhiteSpace(request.FullName))
        {
            customer.FullName = request.FullName.Trim();
        }

        customer.OtpCodeHash = BCrypt.Net.BCrypt.HashPassword(code);
        customer.OtpExpiresAt = DateTime.UtcNow.AddMinutes(10);
        await _context.SaveChangesAsync();

        // Enviar email vía Resend API
        var resendKey = _config["Resend:ApiKey"];
        var fromEmail = _config["Resend:FromEmail"] ?? "onboarding@resend.dev";

        if (!string.IsNullOrEmpty(resendKey))
        {
            var client = _httpClientFactory.CreateClient();
            var url = string.Concat("https://", "api.", "resend.com", "/emails");
            using var req = new HttpRequestMessage(HttpMethod.Post, url);
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", resendKey);
            req.Content = new StringContent(JsonSerializer.Serialize(new
            {
                from = fromEmail,
                to = new[] { email },
                subject = $"Tu código de acceso: {code}",
                html = $"<p>Hola <strong>{customer.FullName}</strong>,</p><p>Tu código de verificación para ingresar a la tienda es:</p><h2 style='letter-spacing:4px;'>{code}</h2><p>Vence en 10 minutos.</p>"
            }), Encoding.UTF8, "application/json");

            var res = await client.SendAsync(req);
            if (!res.IsSuccessStatusCode)
            {
                var err = await res.Content.ReadAsStringAsync();
                _logger.LogError("Error enviando OTP con Resend: {Error}", err);
                return StatusCode(502, new { message = "No se pudo enviar el correo de verificación." });
            }
        }
        else
        {
            // En desarrollo local sin Resend configurado, lo logueamos en consola
            _logger.LogWarning("OTP generado para {Email} (sin Resend configurado): {Code}", email, code);
        }

        return Ok(new { message = "Código enviado a tu correo electrónico." });
    }

    // POST: api/customer-auth/verify-otp
    [HttpPost("verify-otp")]
    public async Task<IActionResult> VerifyOtp(VerifyOtpRequest request)
    {
        var email = request.Email.Trim().ToLowerInvariant();
        var customer = await _context.Customers.FirstOrDefaultAsync(c => c.Email == email);

        if (customer is null || string.IsNullOrEmpty(customer.OtpCodeHash) || customer.OtpExpiresAt < DateTime.UtcNow)
            return BadRequest(new { message = "El código expiró o es inválido. Solicitá uno nuevo." });

        if (!BCrypt.Net.BCrypt.Verify(request.Code.Trim(), customer.OtpCodeHash))
            return BadRequest(new { message = "Código incorrecto." });

        customer.IsEmailVerified = true;
        customer.OtpCodeHash = null;
        customer.OtpExpiresAt = null;
        await _context.SaveChangesAsync();

        SetCustomerCookie(customer);

        return Ok(new { id = customer.Id, email = customer.Email, fullName = customer.FullName });
    }

    [Authorize(Roles = "Customer")]
    [HttpGet("me")]
    public IActionResult Me()
    {
        return Ok(new
        {
            id = User.FindFirstValue(ClaimTypes.NameIdentifier),
            email = User.FindFirstValue(ClaimTypes.Email),
            fullName = User.FindFirstValue(ClaimTypes.Name)
        });
    }

    [HttpPost("logout")]
    public IActionResult Logout()
    {
        Response.Cookies.Delete("customer_token", new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.None
        });
        return Ok();
    }

    private void SetCustomerCookie(Customer customer)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_config["Jwt:Key"]!));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, customer.Id.ToString()),
            new Claim(ClaimTypes.Email, customer.Email),
            new Claim(ClaimTypes.Name, customer.FullName),
            new Claim(ClaimTypes.Role, "Customer")
        };

        var token = new JwtSecurityToken(
            issuer: _config["Jwt:Issuer"],
            audience: _config["Jwt:Audience"],
            claims: claims,
            expires: DateTime.UtcNow.AddDays(7),
            signingCredentials: creds
        );

        var jwt = new JwtSecurityTokenHandler().WriteToken(token);

        Response.Cookies.Append("customer_token", jwt, new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.None,
            Expires = DateTimeOffset.UtcNow.AddDays(7)
        });
    }
}
