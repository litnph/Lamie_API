using Lamie.API.Services;
using Lamie.Application.Addresses;
using Lamie.Domain.Entities;
using Lamie.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Xunit;

namespace Lamie.Tests.Integration;

public sealed class AdministrativeAddressLamieDevRuntimeTests
{
    [Fact]
    public async Task Opted_in_development_database_resolves_required_real_dataset_examples()
    {
        var connectionString = Environment.GetEnvironmentVariable("LAMIE_ADDRESS_RUNTIME_CONNECTION");
        if (string.IsNullOrWhiteSpace(connectionString))
            return;

        var dbOptions = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(connectionString)
            .UseSnakeCaseNamingConvention()
            .Options;
        await using var dbContext = new AppDbContext(dbOptions);
        await dbContext.Database.OpenConnectionAsync();
        await using (var command = dbContext.Database.GetDbConnection().CreateCommand())
        {
            command.CommandText = "SELECT DB_NAME()";
            Assert.Equal("Lamie_Dev", await command.ExecuteScalarAsync());
        }

        using var memoryCache = new MemoryCache(new MemoryCacheOptions());
        var service = new AdministrativeAddressService(
            dbContext,
            memoryCache,
            Options.Create(new AdministrativeAddressResolutionOptions
            {
                DefaultProvinceCode = "79",
                CandidateLimit = 8,
                ConfidentThreshold = 0.82m
            }));

        var current = await service.ResolveAsync(
            new ResolveAddressRequest { Text = "80/3 Nguyễn Trãi, phường Chợ Quán" },
            CancellationToken.None);
        Assert.NotNull(current.SelectedCandidate);
        Assert.Equal(AdministrativeScheme.Current, current.SelectedCandidate.Scheme);
        Assert.Equal("79", current.SelectedCandidate.ProvinceCode);
        Assert.Equal("27301", current.SelectedCandidate.CommuneCode);
        Assert.True(current.SelectedCandidate.UsedDefaultProvince);

        var legacy = await service.ResolveAsync(
            new ResolveAddressRequest { Text = "Cầu ba Tây, xã Thường Lạc, huyện Hồng Ngự, Đồng Tháp" },
            CancellationToken.None);
        Assert.NotNull(legacy.SelectedCandidate);
        Assert.Equal(AdministrativeScheme.Legacy, legacy.SelectedCandidate.Scheme);
        Assert.Equal("87", legacy.SelectedCandidate.ProvinceCode);
        Assert.Equal("870", legacy.SelectedCandidate.DistrictCode);
        Assert.Equal("29977", legacy.SelectedCandidate.CommuneCode);
        Assert.DoesNotContain(legacy.Candidates, candidate => candidate.ProvinceCode == "79");

        var ambiguous = await service.ResolveAsync(
            new ResolveAddressRequest { Text = "461 Phan Van Tri, phuong An Nhon" },
            CancellationToken.None);
        Assert.NotNull(ambiguous.SelectedCandidate);
        Assert.Equal(ambiguous.Candidates[0], ambiguous.SelectedCandidate);
        Assert.Equal("79", ambiguous.SelectedCandidate.ProvinceCode);
    }
}
