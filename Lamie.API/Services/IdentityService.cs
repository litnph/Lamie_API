using System.Data;
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
    private readonly IUserPermissionResolver _permissionResolver;
    private readonly IAccessControlCache _accessControlCache;
    private readonly IAccessAuditWriter _auditWriter;

    public IdentityService(
        AppDbContext dbContext,
        IPasswordHasher<User> passwordHasher,
        IJwtTokenService tokenService,
        IHttpContextAccessor httpContextAccessor,
        TimeProvider timeProvider,
        IUserPermissionResolver permissionResolver,
        IAccessControlCache accessControlCache,
        IAccessAuditWriter auditWriter)
    {
        _dbContext = dbContext;
        _passwordHasher = passwordHasher;
        _tokenService = tokenService;
        _httpContextAccessor = httpContextAccessor;
        _timeProvider = timeProvider;
        _permissionResolver = permissionResolver;
        _accessControlCache = accessControlCache;
        _auditWriter = auditWriter;
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

        var authorization = await GetAuthorizationAsync(existing.User, cancellationToken);
        var nextRefresh = _tokenService.CreateRefreshToken();
        existing.Revoke(now, ipAddress, nextRefresh.Hash);
        _dbContext.RefreshTokens.Add(new RefreshToken(
            existing.UserId,
            nextRefresh.Hash,
            now,
            nextRefresh.ExpiresAt,
            ipAddress));

        var access = _tokenService.CreateAccessToken(
            existing.User,
            authorization.PermissionCodes,
            authorization.RoleCode);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return new AuthResultDto(
            ToDto(existing.User, authorization),
            new AuthTokensDto(access.Token, access.ExpiresAt, nextRefresh.Token, nextRefresh.ExpiresAt));
    }

    public async Task<AuthUserDto> GetCurrentUserAsync(CancellationToken cancellationToken)
    {
        var user = await GetCurrentUserEntityAsync(cancellationToken);
        return ToDto(user, await GetAuthorizationAsync(user, cancellationToken));
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
        var users = await _dbContext.Users
            .AsNoTracking()
            .OrderBy(user => user.FullName)
            .ThenBy(user => user.Email)
            .ToListAsync(cancellationToken);
        var assignments = await (
                from userRole in _dbContext.UserRoles.AsNoTracking()
                join role in _dbContext.Roles.AsNoTracking() on userRole.RoleId equals role.Id
                select new { userRole.UserId, Role = role })
            .ToDictionaryAsync(item => item.UserId, cancellationToken);

        return users.Select(user =>
        {
            assignments.TryGetValue(user.Id, out var assignment);
            var roleId = assignment?.Role.Id ?? Role.IdFor(user.Role);
            return new AuthUserDto(
                user.Id,
                user.Email,
                user.UserName,
                user.FullName,
                user.Phone,
                user.Role,
                user.IsActive,
                user.LastLoginAt,
                user.CreatedAt,
                null,
                roleId,
                assignment?.Role.Name ?? user.Role.ToString(),
                assignment?.Role.Code ?? user.Role.ToString().ToLowerInvariant());
        }).ToList();
    }

    public async Task<AuthUserDto> GetUserAsync(Guid id, CancellationToken cancellationToken)
    {
        var user = await _dbContext.Users.AsNoTracking().SingleOrDefaultAsync(item => item.Id == id, cancellationToken)
            ?? throw new NotFoundException("User", id);
        return ToDto(user, await GetAuthorizationAsync(user, cancellationToken));
    }

    public async Task<AuthUserDto> CreateUserAsync(CreateUserRequest request, CancellationToken cancellationToken)
    {
        ValidateEmail(request.Email);
        ValidateRequired(request.UserName, nameof(request.UserName));
        ValidateRequired(request.FullName, nameof(request.FullName));
        ValidatePassword(request.Password, nameof(request.Password));
        ValidateRole(request.Role);
        var selectedRole = await ResolveRoleAsync(request.RoleId, request.Role, cancellationToken);

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
            Role.LegacyValueFor(selectedRole.Id),
            request.IsActive,
            now);
        user.SetPasswordHash(_passwordHasher.HashPassword(user, request.Password));
        _dbContext.Users.Add(user);
        _dbContext.UserRoles.Add(new UserRole(user.Id, selectedRole.Id, now));
        _auditWriter.Record(
            "assign",
            "UserRole",
            user.Id.ToString(),
            null,
            new { UserId = user.Id, RoleId = selectedRole.Id });
        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            throw new ConflictException("Email or username is already in use.");
        }

        _accessControlCache.InvalidateUser(user.Id);
        return ToDto(user, await GetAuthorizationAsync(user, cancellationToken));
    }

    public async Task UpdateUserAsync(Guid id, UpdateUserRequest request, CancellationToken cancellationToken)
    {
        if (request.Id != Guid.Empty && request.Id != id)
            throw Validation(nameof(request.Id), "Route id and body id must match.");
        ValidateRequired(request.FullName, nameof(request.FullName));
        ValidateRole(request.Role);

        await using var transaction = await _dbContext.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);
        var user = await _dbContext.Users.SingleOrDefaultAsync(item => item.Id == id, cancellationToken)
            ?? throw new NotFoundException("User", id);
        var currentUserId = GetCurrentUserId();
        if (user.Id == currentUserId && !request.IsActive)
            throw new ConflictException("You cannot disable your own account.");

        var selectedRole = await ResolveRoleAsync(request.RoleId, request.Role, cancellationToken);
        var assignment = await _dbContext.UserRoles.SingleOrDefaultAsync(item => item.UserId == id, cancellationToken);
        var currentRoleId = assignment?.RoleId ?? Role.IdFor(user.Role);
        if (user.Id == currentUserId && currentRoleId != selectedRole.Id)
            throw new ConflictException("You cannot change your own role.");
        if (currentRoleId == Role.AdminId && (selectedRole.Id != Role.AdminId || !request.IsActive))
            await EnsureAnotherActiveAdministratorAsync(user.Id, cancellationToken);

        var now = UtcNow();
        user.UpdateProfile(
            request.FullName,
            request.Phone,
            Role.LegacyValueFor(selectedRole.Id),
            request.IsActive,
            now);
        if (assignment is null)
        {
            _dbContext.UserRoles.Add(new UserRole(id, selectedRole.Id, now));
            _auditWriter.Record(
                "assign",
                "UserRole",
                id.ToString(),
                null,
                new { UserId = id, RoleId = selectedRole.Id });
        }
        else if (assignment.RoleId != selectedRole.Id)
        {
            var previousRoleId = assignment.RoleId;
            _dbContext.UserRoles.Remove(assignment);
            _dbContext.UserRoles.Add(new UserRole(id, selectedRole.Id, now));
            _auditWriter.Record(
                "assign",
                "UserRole",
                id.ToString(),
                new { UserId = id, RoleId = previousRoleId },
                new { UserId = id, RoleId = selectedRole.Id });
        }

        if (!request.IsActive || currentRoleId != selectedRole.Id)
            await RevokeAllTokensAsync(user.Id, now, null, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        _accessControlCache.InvalidateUser(user.Id);
    }

    public async Task DisableUserAsync(Guid id, string? ipAddress, CancellationToken cancellationToken)
    {
        if (id == GetCurrentUserId())
            throw new ConflictException("You cannot disable your own account.");
        await using var transaction = await _dbContext.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);
        var user = await _dbContext.Users.SingleOrDefaultAsync(item => item.Id == id, cancellationToken)
            ?? throw new NotFoundException("User", id);
        var assignedRoleId = await _dbContext.UserRoles
            .Where(item => item.UserId == id)
            .Select(item => (Guid?)item.RoleId)
            .SingleOrDefaultAsync(cancellationToken);
        if ((assignedRoleId ?? Role.IdFor(user.Role)) == Role.AdminId)
            await EnsureAnotherActiveAdministratorAsync(user.Id, cancellationToken);
        user.UpdateProfile(user.FullName, user.Phone, user.Role, false, UtcNow());
        await RevokeAllTokensAsync(user.Id, UtcNow(), ipAddress, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        _accessControlCache.InvalidateUser(user.Id);
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
        var authorization = await GetAuthorizationAsync(user, cancellationToken);
        var access = _tokenService.CreateAccessToken(user, authorization.PermissionCodes, authorization.RoleCode);
        var refresh = _tokenService.CreateRefreshToken();
        _dbContext.RefreshTokens.Add(new RefreshToken(user.Id, refresh.Hash, now, refresh.ExpiresAt, ipAddress));
        await _dbContext.SaveChangesAsync(cancellationToken);
        return new AuthResultDto(
            ToDto(user, authorization),
            new AuthTokensDto(access.Token, access.ExpiresAt, refresh.Token, refresh.ExpiresAt));
    }

    private async Task<AuthorizationSnapshot> GetAuthorizationAsync(
        User user,
        CancellationToken cancellationToken)
    {
        var snapshot = await _permissionResolver.ResolveAsync(user.Id, cancellationToken);
        return new AuthorizationSnapshot(
            snapshot.RoleId,
            snapshot.RoleCode,
            snapshot.RoleName,
            snapshot.PermissionCodes);
    }

    private async Task<Role> ResolveRoleAsync(
        Guid? requestedRoleId,
        BuiltInRole legacyRole,
        CancellationToken cancellationToken)
    {
        var roleId = requestedRoleId ?? Role.IdFor(legacyRole);
        return await _dbContext.Roles.SingleOrDefaultAsync(role => role.Id == roleId && role.IsActive, cancellationToken)
            ?? throw Validation(nameof(requestedRoleId), "Role does not exist or is inactive.");
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

    private async Task EnsureAnotherActiveAdministratorAsync(
        Guid excludedUserId,
        CancellationToken cancellationToken)
    {
        var hasAnotherAdministrator = await _dbContext.Users.AsNoTracking().AnyAsync(
            candidate => candidate.Id != excludedUserId
                && candidate.Status == UserStatus.Active
                && (_dbContext.UserRoles.Any(assignment =>
                        assignment.UserId == candidate.Id && assignment.RoleId == Role.AdminId)
                    || (!_dbContext.UserRoles.Any(assignment => assignment.UserId == candidate.Id)
                        && candidate.Role == BuiltInRole.Admin)),
            cancellationToken);
        if (!hasAnotherAdministrator)
            throw new ConflictException("The final active administrator account cannot be disabled or reassigned.");
    }

    private DateTime UtcNow() => _timeProvider.GetUtcNow().UtcDateTime;

    private static AuthUserDto ToDto(User user, AuthorizationSnapshot authorization) => new(
        user.Id,
        user.Email,
        user.UserName,
        user.FullName,
        user.Phone,
        user.Role,
        user.IsActive,
        user.LastLoginAt,
        user.CreatedAt,
        authorization.PermissionCodes,
        authorization.RoleId,
        authorization.RoleName,
        authorization.RoleCode);

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

    private static void ValidateRole(BuiltInRole role)
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

    private sealed record AuthorizationSnapshot(
        Guid RoleId,
        string RoleCode,
        string RoleName,
        IReadOnlyCollection<string> PermissionCodes);
}
