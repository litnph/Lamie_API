using Lamie.API.Options;
using Lamie.Domain.Entities;
using Lamie.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Lamie.API.Services;

public sealed class BootstrapAdminHostedService : IHostedService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly BootstrapAdminOptions _options;
    private readonly TimeProvider _timeProvider;

    public BootstrapAdminHostedService(
        IServiceProvider serviceProvider,
        IOptions<BootstrapAdminOptions> options,
        TimeProvider timeProvider)
    {
        _serviceProvider = serviceProvider;
        _options = options.Value;
        _timeProvider = timeProvider;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_options.Email)
            || string.IsNullOrWhiteSpace(_options.UserName)
            || string.IsNullOrWhiteSpace(_options.Password))
        {
            return;
        }

        using var scope = _serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var normalizedEmail = User.Normalize(_options.Email);
        if (await dbContext.Users.AnyAsync(user => user.NormalizedEmail == normalizedEmail, cancellationToken))
            return;

        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var user = new User(
            _options.Email,
            _options.UserName,
            "pending-password-hash",
            _options.FullName,
            null,
            BuiltInRole.Admin,
            true,
            now);
        var passwordHasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher<User>>();
        user.SetPasswordHash(passwordHasher.HashPassword(user, _options.Password));
        dbContext.Users.Add(user);
        dbContext.UserRoles.Add(new UserRole(user.Id, Role.AdminId, now));
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
