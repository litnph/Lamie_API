using System.Security.Claims;
using Lamie.Application.Common.Exceptions;
using Lamie.Application.Identity;
using Lamie.Domain.Entities;
using Lamie.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Lamie.API.Services;

public sealed class IdentityService : IIdentityService
{
    private const string InvalidCredentialsMessage = "Invalid login or password.";
    private readonly AppDbContext _dbContext;
    private readonly IPasswordHasher<User> _passwordHasher;
    private readonly IJwtTokenService _tokenService;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly TimeProvider _timeProvider;

    public IdentityService(
        AppDbContext dbContext,
        IPasswordHasher<User> passwordHasher,
        IJwtTokenService tokenService,
        IHttpContextAccessor httpContextAccessor,
        TimeProvider timeProvider)
    {
        _dbContext = dbContext;
        _passwordHasher = passwordHasher;
        _tokenService = tokenService;
        _httpContextAccessor = httpContextAccessor;
        _timeProvider = timeProvider;
    }

    public async Task<AuthResultDto> LoginAsync(
        LoginRequest request,
        string? ipAddress,
        CancellationToken cancellationToken)
    {
        ValidateRequired(request.Login, nameof(request.Login));
        ValidateRequired(request.Password, nameof(request.Password));

        var normalizedLogin = User.Normalize(request.Login);
        var user = await _dbContext.Users.SingleOrDefaultAsync(
            candidate => candidate.NormalizedEmail == normalizedLogin || candidate.NormalizedUserName == normalizedLogin,
            cancellationToken);
        if (user is null)
            throw new UnauthorizedException(InvalidCredentialsMessage);

        var now = UtcNow();
        if (!user.CanAttemptLogin(now))
            throw new UnauthorizedException(InvalidCredentialsMessage);

        var verification = _passwordHasher.VerifyHashedPassword(user, user.PasswordHash, request.Password);
        if (verification == PasswordVerificationResult.Failed)
        {
            user.RecordFailedLogin(now);
            await _dbContext.SaveChangesAsync(cancellationToken);
            throw new UnauthorizedException(InvalidCredentialsMessage);
        }

        if (verification == PasswordVerificationResult.SuccessRehashNeeded)
            user.SetPasswordHash(_passwordHasher.HashPassword(user, request.Password));

        user.RecordSuccessfulLogin(now);
        return await CreateSessionAsync(user, ipAddress, cancellationToken);
    }

    public async Task<AuthResultDto> RefreshAsync(
        RefreshRequest request,
        string? ipAddress,
        CancellationToken cancellationToken)
    {
        ValidateRequired(request.RefreshToken, nameof(request.RefreshToken));
        var now = UtcNow();
        var tokenHash = _tokenService.HashRefreshToken(request.RefreshToken);
        var existing = await _dbContext.RefreshTokens
            .Include(token => token.User)
            .SingleOrDefaultAsync(token => token.TokenHash == tokenHash, cancellationToken);
        if (existing is null)
            throw new UnauthorizedException("Invalid refresh token.");

        if (existing.RevokedAt is not null)
        {
            if (!string.IsNullOrWhiteSpace(existing.ReplacedByTokenHash))
            {
                var activeTokens = await _dbContext.RefreshTokens
                    .Where(token => token.UserId == existing.UserId && token.RevokedAt == null && token.ExpiresAt > now)
                    .ToListAsync(cancellationToken);
                foreach (var token in activeTokens)
                    token.Revoke(now, ipAddress);
                await _dbContext.SaveChangesAsync(cancellationToken);
            }

            throw new UnauthorizedException("Invalid refresh token.");
        }

        if (!existing.IsActive(now) || !existing.User.CanAttemptLogin(now))
        {
            existing.Revoke(now, ipAddress);
            await _dbContext.SaveChangesAsync(cancellationToken);
            throw new UnauthorizedException("Invalid refresh token.");
        }

        var nextRefresh = _tokenService.CreateRefreshToken();
        existing.Revoke(now, ipAddress, nextRefresh.Hash);
        var replacement = new RefreshToken(
            existing.UserId,
            nextRefresh.Hash,
            now,
            nextRefresh.ExpiresAt,
            ipAddress);
        _dbContext.RefreshTokens.Add(replacement);

        var access = _tokenService.CreateAccessToken(existing.User, RolePermissions.Get(existing.User.Role));
        await _dbContext.SaveChangesAsync(cancellationToken);
        return new AuthResultDto(
            ToDto(existing.User),
            new AuthTokensDto(access.Token, access.ExpiresAt, nextRefresh.Token, nextRefresh.ExpiresAt));
    }

    public async Task<AuthUserDto> GetCurrentUserAsync(CancellationToken cancellationToken)
    {
        var user = await GetCurrentUserEntityAsync(cancellationToken);
        return ToDto(user);
    }

    public async Task LogoutAsync(
        LogoutRequest request,
        string? ipAddress,
        CancellationToken cancellationToken)
    {
        ValidateRequired(request.RefreshToken, nameof(request.RefreshToken));
        var userId = GetCurrentUserId();
        var tokenHash = _tokenService.HashRefreshToken(request.RefreshToken);
        var token = await _dbContext.RefreshTokens.SingleOrDefaultAsync(
            candidate => candidate.UserId == userId && candidate.TokenHash == tokenHash,
            cancellationToken);
        if (token is not null)
        {
            token.Revoke(UtcNow(), ipAddress);
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task ChangePasswordAsync(
        ChangePasswordRequest request,
        string? ipAddress,
        CancellationToken cancellationToken)
    {
        ValidatePassword(request.NewPassword, nameof(request.NewPassword));
        ValidateRequired(request.CurrentPassword, nameof(request.CurrentPassword));
        var user = await GetCurrentUserEntityAsync(cancellationToken);
        var verification = _passwordHasher.VerifyHashedPassword(user, user.PasswordHash, request.CurrentPassword);
        if (verification == PasswordVerificationResult.Failed)
            throw new UnauthorizedException("Current password is invalid.");

        user.SetPasswordHash(_passwordHasher.HashPassword(user, request.NewPassword));
        await RevokeAllTokensAsync(user.Id, UtcNow(), ipAddress, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<AuthUserDto>> GetUsersAsync(CancellationToken cancellationToken)
    {
        return await _dbContext.Users
            .AsNoTracking()
            .OrderBy(user => user.FullName)
            .ThenBy(user => user.Email)
            .Select(user => new AuthUserDto(
                user.Id,
                user.Email,
                user.UserName,
                user.FullName,
                user.Phone,
                user.Role,
                user.Status == UserStatus.Active,
                user.LastLoginAt,
                user.CreatedAt,
                null))
            .ToListAsync(cancellationToken);
    }

    public async Task<AuthUserDto> GetUserAsync(Guid id, CancellationToken cancellationToken)
    {
        var user = await _dbContext.Users.AsNoTracking().SingleOrDefaultAsync(item => item.Id == id, cancellationToken)
            ?? throw new NotFoundException("User", id);
        return ToDto(user);
    }

    public async Task<AuthUserDto> CreateUserAsync(CreateUserRequest request, CancellationToken cancellationToken)
    {
        ValidateEmail(request.Email);
        ValidateRequired(request.UserName, nameof(request.UserName));
        ValidateRequired(request.FullName, nameof(request.FullName));
        ValidatePassword(request.Password, nameof(request.Password));
        ValidateRole(request.Role);

        var normalizedEmail = User.Normalize(request.Email);
        var normalizedUserName = User.Normalize(request.UserName);
        if (await _dbContext.Users.AnyAsync(user => user.NormalizedEmail == normalizedEmail, cancellationToken))
            throw new ConflictException("Email is already in use.");
        if (await _dbContext.Users.AnyAsync(user => user.NormalizedUserName == normalizedUserName, cancellationToken))
            throw new ConflictException("Username is already in use.");

        var now = UtcNow();
        var user = new User(
            request.Email,
            request.UserName,
            "pending-password-hash",
            request.FullName,
            request.Phone,
            request.Role,
            request.IsActive,
            now);
        user.SetPasswordHash(_passwordHasher.HashPassword(user, request.Password));
        _dbContext.Users.Add(user);
        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            throw new ConflictException("Email or username is already in use.");
        }

        return ToDto(user);
    }

    public async Task UpdateUserAsync(Guid id, UpdateUserRequest request, CancellationToken cancellationToken)
    {
        if (request.Id != Guid.Empty && request.Id != id)
            throw Validation(nameof(request.Id), "Route id and body id must match.");
        ValidateRequired(request.FullName, nameof(request.FullName));
        ValidateRole(request.Role);

        var user = await _dbContext.Users.SingleOrDefaultAsync(item => item.Id == id, cancellationToken)
            ?? throw new NotFoundException("User", id);
        if (user.Id == GetCurrentUserId() && !request.IsActive)
            throw new ConflictException("You cannot disable your own account.");

        user.UpdateProfile(request.FullName, request.Phone, request.Role, request.IsActive, UtcNow());
        if (!request.IsActive)
            await RevokeAllTokensAsync(user.Id, UtcNow(), null, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task DisableUserAsync(Guid id, string? ipAddress, CancellationToken cancellationToken)
    {
        if (id == GetCurrentUserId())
            throw new ConflictException("You cannot disable your own account.");
        var user = await _dbContext.Users.SingleOrDefaultAsync(item => item.Id == id, cancellationToken)
            ?? throw new NotFoundException("User", id);
        user.UpdateProfile(user.FullName, user.Phone, user.Role, false, UtcNow());
        await RevokeAllTokensAsync(user.Id, UtcNow(), ipAddress, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task ResetPasswordAsync(
        Guid id,
        ResetPasswordRequest request,
        string? ipAddress,
        CancellationToken cancellationToken)
    {
        ValidatePassword(request.NewPassword, nameof(request.NewPassword));
        var user = await _dbContext.Users.SingleOrDefaultAsync(item => item.Id == id, cancellationToken)
            ?? throw new NotFoundException("User", id);
        user.SetPasswordHash(_passwordHasher.HashPassword(user, request.NewPassword));
        await RevokeAllTokensAsync(user.Id, UtcNow(), ipAddress, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task<AuthResultDto> CreateSessionAsync(
        User user,
        string? ipAddress,
        CancellationToken cancellationToken)
    {
        var now = UtcNow();
        var access = _tokenService.CreateAccessToken(user, RolePermissions.Get(user.Role));
        var refresh = _tokenService.CreateRefreshToken();
        _dbContext.RefreshTokens.Add(new RefreshToken(user.Id, refresh.Hash, now, refresh.ExpiresAt, ipAddress));
        await _dbContext.SaveChangesAsync(cancellationToken);
        return new AuthResultDto(
            ToDto(user),
            new AuthTokensDto(access.Token, access.ExpiresAt, refresh.Token, refresh.ExpiresAt));
    }

    private async Task<User> GetCurrentUserEntityAsync(CancellationToken cancellationToken)
    {
        var id = GetCurrentUserId();
        var user = await _dbContext.Users.SingleOrDefaultAsync(item => item.Id == id, cancellationToken)
            ?? throw new UnauthorizedException();
        if (!user.CanAttemptLogin(UtcNow()))
            throw new UnauthorizedException();
        return user;
    }

    private Guid GetCurrentUserId()
    {
        var principal = _httpContextAccessor.HttpContext?.User;
        var value = principal?.FindFirstValue("sub") ?? principal?.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(value, out var id) ? id : throw new UnauthorizedException();
    }

    private async Task RevokeAllTokensAsync(
        Guid userId,
        DateTime now,
        string? ipAddress,
        CancellationToken cancellationToken)
    {
        var tokens = await _dbContext.RefreshTokens
            .Where(token => token.UserId == userId && token.RevokedAt == null && token.ExpiresAt > now)
            .ToListAsync(cancellationToken);
        foreach (var token in tokens)
            token.Revoke(now, ipAddress);
    }

    private DateTime UtcNow() => _timeProvider.GetUtcNow().UtcDateTime;

    private static AuthUserDto ToDto(User user) => new(
        user.Id,
        user.Email,
        user.UserName,
        user.FullName,
        user.Phone,
        user.Role,
        user.IsActive,
        user.LastLoginAt,
        user.CreatedAt,
        RolePermissions.Get(user.Role));

    private static void ValidateRequired(string? value, string field)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw Validation(field, $"{field} is required.");
    }

    private static void ValidateEmail(string email)
    {
        ValidateRequired(email, nameof(email));
        if (!System.Net.Mail.MailAddress.TryCreate(email, out _))
            throw Validation(nameof(email), "Email is invalid.");
    }

    private static void ValidateRole(UserRole role)
    {
        if (!Enum.IsDefined(role))
            throw Validation(nameof(role), "Role is invalid.");
    }

    private static void ValidatePassword(string password, string field)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(password) || password.Length < 8)
            errors.Add("Password must contain at least 8 characters.");
        if (!password.Any(char.IsUpper))
            errors.Add("Password must contain an uppercase letter.");
        if (!password.Any(char.IsLower))
            errors.Add("Password must contain a lowercase letter.");
        if (!password.Any(char.IsDigit))
            errors.Add("Password must contain a number.");
        if (errors.Count > 0)
            throw new ValidationException(new Dictionary<string, string[]> { [field] = errors.ToArray() });
    }

    private static ValidationException Validation(string field, string message) =>
        new(new Dictionary<string, string[]> { [field] = [message] });
}
