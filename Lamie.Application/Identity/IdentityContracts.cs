using Lamie.Domain.Entities;

namespace Lamie.Application.Identity;

public sealed record LoginRequest(string Login, string Password);
public sealed record RefreshRequest(string RefreshToken);
public sealed record LogoutRequest(string RefreshToken);
public sealed record ChangePasswordRequest(string CurrentPassword, string NewPassword);
public sealed record ResetPasswordRequest(string NewPassword);

public sealed record CreateUserRequest(
    string Email,
    string UserName,
    string Password,
    string FullName,
    string? Phone,
    UserRole Role,
    bool IsActive);

public sealed record UpdateUserRequest(
    Guid Id,
    string FullName,
    string? Phone,
    UserRole Role,
    bool IsActive);

public sealed record AuthTokensDto(
    string AccessToken,
    DateTime AccessTokenExpiresAt,
    string RefreshToken,
    DateTime RefreshTokenExpiresAt);

public sealed record AuthUserDto(
    Guid Id,
    string Email,
    string UserName,
    string FullName,
    string? Phone,
    UserRole Role,
    bool IsActive,
    DateTime? LastLoginAt,
    DateTime CreatedAt,
    IReadOnlyCollection<string>? Permissions = null);

public sealed record AuthResultDto(AuthUserDto User, AuthTokensDto Tokens);

public sealed record AccessTokenResult(string Token, DateTime ExpiresAt);
public sealed record RefreshTokenResult(string Token, string Hash, DateTime ExpiresAt);

public interface IJwtTokenService
{
    AccessTokenResult CreateAccessToken(User user, IEnumerable<string>? permissions = null);
    RefreshTokenResult CreateRefreshToken();
    string HashRefreshToken(string token);
}

public interface IIdentityService
{
    Task<AuthResultDto> LoginAsync(LoginRequest request, string? ipAddress, CancellationToken cancellationToken);
    Task<AuthResultDto> RefreshAsync(RefreshRequest request, string? ipAddress, CancellationToken cancellationToken);
    Task<AuthUserDto> GetCurrentUserAsync(CancellationToken cancellationToken);
    Task LogoutAsync(LogoutRequest request, string? ipAddress, CancellationToken cancellationToken);
    Task ChangePasswordAsync(ChangePasswordRequest request, string? ipAddress, CancellationToken cancellationToken);
    Task<IReadOnlyList<AuthUserDto>> GetUsersAsync(CancellationToken cancellationToken);
    Task<AuthUserDto> GetUserAsync(Guid id, CancellationToken cancellationToken);
    Task<AuthUserDto> CreateUserAsync(CreateUserRequest request, CancellationToken cancellationToken);
    Task UpdateUserAsync(Guid id, UpdateUserRequest request, CancellationToken cancellationToken);
    Task DisableUserAsync(Guid id, string? ipAddress, CancellationToken cancellationToken);
    Task ResetPasswordAsync(Guid id, ResetPasswordRequest request, string? ipAddress, CancellationToken cancellationToken);
}
