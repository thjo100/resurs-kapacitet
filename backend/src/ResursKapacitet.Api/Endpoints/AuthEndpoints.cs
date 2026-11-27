using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;
using ResursKapacitet.Api.Data;
using ResursKapacitet.Api.Services;

namespace ResursKapacitet.Api.Endpoints;

public record LoginRequest(string UserName, string Password);

public record ChangePasswordRequest(string CurrentPassword, string NewPassword);

public record MeDto(int Id, string UserName, string DisplayName, UserRole Role, bool MustChangePassword);

public static class AuthEndpoints
{
    public static void MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/auth");

        g.MapPost("/login", async (LoginRequest req, AppDbContext db, HttpContext http) =>
        {
            var userName = (req.UserName ?? "").Trim().ToLowerInvariant();
            var user = await db.Users.FirstOrDefaultAsync(u => u.UserName == userName && u.IsActive);
            if (user is null || !Passwords.Verify(user, req.Password ?? ""))
                return Results.Problem("Fel användarnamn eller lösenord.", statusCode: 401);

            user.LastLoginAt = DateTime.UtcNow;
            await db.SaveChangesAsync();

            var claims = new List<Claim>
            {
                new(ClaimTypes.NameIdentifier, user.Id.ToString()),
                new(ClaimTypes.Name, user.UserName),
                new(ClaimsPrincipalExtensions.DisplayNameClaim, user.DisplayName),
                new(ClaimTypes.Role, user.Role.ToString()),
            };
            var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme));
            await http.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal,
                new AuthenticationProperties { IsPersistent = true });
            return Results.Ok(new MeDto(user.Id, user.UserName, user.DisplayName, user.Role, user.MustChangePassword));
        }).RequireRateLimiting("login");

        g.MapPost("/logout", async (HttpContext http) =>
        {
            await http.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return Results.NoContent();
        });

        g.MapGet("/me", async (ClaimsPrincipal principal, AppDbContext db) =>
        {
            if (principal.UserId() is not { } id) return Results.Ok((MeDto?)null);
            var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == id && u.IsActive);
            return Results.Ok(user is null
                ? null
                : new MeDto(user.Id, user.UserName, user.DisplayName, user.Role, user.MustChangePassword));
        });

        g.MapPost("/password", async (ChangePasswordRequest req, ClaimsPrincipal principal, AppDbContext db) =>
        {
            if (principal.UserId() is not { } id) return Results.Unauthorized();
            var user = await db.Users.FirstOrDefaultAsync(u => u.Id == id);
            if (user is null) return Results.Unauthorized();
            if (!Passwords.Verify(user, req.CurrentPassword ?? ""))
                return Results.Problem("Nuvarande lösenord stämmer inte.", statusCode: 400);
            if ((req.NewPassword ?? "").Length < 6)
                return Results.Problem("Lösenordet måste vara minst 6 tecken.", statusCode: 400);
            user.PasswordHash = Passwords.Hash(user, req.NewPassword!);
            user.MustChangePassword = false;
            db.Log(principal, "Användare", user.Id, "Ändrad", $"{user.DisplayName} bytte lösenord");
            await db.SaveChangesAsync();
            return Results.NoContent();
        }).RequireAuthorization();
    }
}
