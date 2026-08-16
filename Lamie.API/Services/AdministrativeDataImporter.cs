using System.Text.Json;
using System.Text.Json.Serialization;
using Lamie.Application.Addresses;
using Lamie.Domain.Entities;
using Lamie.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Lamie.API.Services;

public sealed class AdministrativeDataOptions
{
    public const string SectionName = "AdministrativeData";
    public bool ImportOnStartup { get; init; }
    public string RootPath { get; init; } = "Data/AdministrativeUnits";
}

public sealed record AdministrativeDataImportReport(
    int CurrentProvinceCount,
    int CurrentCommuneCount,
    int LegacyProvinceCount,
    int LegacyDistrictCount,
    int LegacyCommuneCount,
    int TransitionCount,
    int DuplicateCount,
    int InvalidParentCount,
    int InvalidTransitionCount);

public interface IAdministrativeDataImporter
{
    Task<AdministrativeDataImportReport> ImportAsync(CancellationToken cancellationToken);
}

public sealed class AdministrativeDataImporter : IAdministrativeDataImporter
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly AppDbContext _dbContext;
    private readonly AdministrativeDataOptions _options;
    private readonly IWebHostEnvironment _environment;
    private readonly ILogger<AdministrativeDataImporter> _logger;

    public AdministrativeDataImporter(
        AppDbContext dbContext,
        Microsoft.Extensions.Options.IOptions<AdministrativeDataOptions> options,
        IWebHostEnvironment environment,
        ILogger<AdministrativeDataImporter> logger)
    {
        _dbContext = dbContext;
        _options = options.Value;
        _environment = environment;
        _logger = logger;
    }

    public async Task<AdministrativeDataImportReport> ImportAsync(CancellationToken cancellationToken)
    {
        var root = Path.GetFullPath(Path.Combine(_environment.ContentRootPath, _options.RootPath));
        if (!Directory.Exists(root))
            throw new InvalidOperationException($"Administrative data directory does not exist: {root}");

        var unitFiles = Directory.GetFiles(root, "administrative-units.*.json")
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();
        if (unitFiles.Length == 0)
            throw new InvalidOperationException("No administrative unit datasets were found.");

        var datasets = new List<AdministrativeUnitDataset>();
        foreach (var file in unitFiles)
        {
            await using var stream = File.OpenRead(file);
            var dataset = await JsonSerializer.DeserializeAsync<AdministrativeUnitDataset>(stream, JsonOptions, cancellationToken)
                ?? throw new InvalidOperationException($"Administrative dataset is empty: {file}");
            ValidateDataset(dataset, file);
            datasets.Add(dataset);
        }

        var allUnits = datasets.SelectMany(dataset => dataset.Units.Select(unit => (dataset, unit))).ToList();
        var duplicateCount = allUnits
            .GroupBy(item => (item.dataset.Scheme, item.unit.Code))
            .Count(group => group.Count() > 1);
        if (duplicateCount > 0)
            throw new InvalidOperationException($"Administrative datasets contain {duplicateCount} duplicate scheme/code values.");
        var unitLookup = allUnits.ToDictionary(item => (item.dataset.Scheme, item.unit.Code));
        var invalidParentCount = allUnits.Count(item => item.unit.HierarchyLevel > 1
            && (item.unit.ParentCode is null
                || !unitLookup.TryGetValue((item.dataset.Scheme, item.unit.ParentCode), out var parent)
                || parent.unit.HierarchyLevel != item.unit.HierarchyLevel - 1));
        if (invalidParentCount > 0)
            throw new InvalidOperationException($"Administrative datasets contain {invalidParentCount} invalid parents.");

        foreach (var dataset in datasets)
            await ImportUnitsAsync(dataset, cancellationToken);

        var transitionFile = Path.Combine(root, "administrative-unit-transitions.json");
        var transitions = File.Exists(transitionFile)
            ? await ReadTransitionsAsync(transitionFile, cancellationToken)
            : new AdministrativeTransitionDataset();
        var invalidTransitionCount = transitions.Transitions.Count(transition =>
            !unitLookup.TryGetValue((AdministrativeScheme.Legacy, transition.LegacyUnitCode), out var legacy)
            || legacy.unit.HierarchyLevel != 3
            || !unitLookup.TryGetValue((AdministrativeScheme.Current, transition.CurrentUnitCode), out var current)
            || current.unit.HierarchyLevel != 2);
        if (invalidTransitionCount > 0)
            throw new InvalidOperationException($"Administrative transition dataset contains {invalidTransitionCount} invalid unit references.");
        await ImportTransitionsAsync(transitions, cancellationToken);

        var report = new AdministrativeDataImportReport(
            allUnits.Count(item => item.dataset.Scheme == AdministrativeScheme.Current && item.unit.HierarchyLevel == 1),
            allUnits.Count(item => item.dataset.Scheme == AdministrativeScheme.Current && item.unit.HierarchyLevel == 2),
            allUnits.Count(item => item.dataset.Scheme == AdministrativeScheme.Legacy && item.unit.HierarchyLevel == 1),
            allUnits.Count(item => item.dataset.Scheme == AdministrativeScheme.Legacy && item.unit.HierarchyLevel == 2),
            allUnits.Count(item => item.dataset.Scheme == AdministrativeScheme.Legacy && item.unit.HierarchyLevel == 3),
            transitions.Transitions.Count,
            duplicateCount,
            invalidParentCount,
            invalidTransitionCount);
        _logger.LogInformation(
            "Administrative data imported: current={CurrentProvinceCount}/{CurrentCommuneCount}, legacy={LegacyProvinceCount}/{LegacyDistrictCount}/{LegacyCommuneCount}, transitions={TransitionCount}",
            report.CurrentProvinceCount,
            report.CurrentCommuneCount,
            report.LegacyProvinceCount,
            report.LegacyDistrictCount,
            report.LegacyCommuneCount,
            report.TransitionCount);
        return report;
    }

    private async Task ImportUnitsAsync(AdministrativeUnitDataset dataset, CancellationToken cancellationToken)
    {
        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
        var existing = await _dbContext.AdministrativeUnits
            .Where(unit => unit.Scheme == dataset.Scheme)
            .ToDictionaryAsync(unit => unit.Code, cancellationToken);
        var incomingCodes = dataset.Units.Select(unit => unit.Code).ToHashSet(StringComparer.Ordinal);

        foreach (var item in dataset.Units)
        {
            var name = VietnameseTextNormalizer.Display(item.Name);
            var fullName = VietnameseTextNormalizer.Display(item.FullName);
            var normalizedName = VietnameseTextNormalizer.Search($"{name} {fullName}");
            if (existing.TryGetValue(item.Code, out var unit))
            {
                unit.Update(name, fullName, normalizedName, item.UnitType, item.HierarchyLevel,
                    item.ParentCode, dataset.EffectiveFrom, dataset.EffectiveTo, item.IsActive,
                    dataset.SourceDocument, dataset.SourceReference, dataset.DatasetVersion, item.SortOrder);
            }
            else
            {
                _dbContext.AdministrativeUnits.Add(new AdministrativeUnit(
                    item.Code, name, fullName, normalizedName, dataset.Scheme, item.UnitType,
                    item.HierarchyLevel, item.ParentCode, dataset.EffectiveFrom, dataset.EffectiveTo,
                    item.IsActive, dataset.SourceDocument, dataset.SourceReference,
                    dataset.DatasetVersion, item.SortOrder));
            }
        }

        foreach (var obsolete in existing.Values.Where(unit => unit.IsActive && !incomingCodes.Contains(unit.Code)))
        {
            obsolete.Update(obsolete.Name, obsolete.FullName, obsolete.NormalizedName, obsolete.UnitType,
                obsolete.HierarchyLevel, obsolete.ParentCode, obsolete.EffectiveFrom,
                dataset.EffectiveFrom.AddDays(-1), false, obsolete.SourceDocument,
                obsolete.SourceReference, obsolete.DatasetVersion, obsolete.SortOrder);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private async Task<AdministrativeTransitionDataset> ReadTransitionsAsync(
        string path,
        CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        var dataset = await JsonSerializer.DeserializeAsync<AdministrativeTransitionDataset>(stream, JsonOptions, cancellationToken)
            ?? throw new InvalidOperationException($"Administrative transition dataset is empty: {path}");
        var duplicateCount = dataset.Transitions
            .GroupBy(item => (item.LegacyUnitCode, item.CurrentUnitCode))
            .Count(group => group.Count() > 1);
        if (duplicateCount > 0)
            throw new InvalidOperationException($"Administrative transition dataset contains {duplicateCount} duplicate mappings.");
        return dataset;
    }

    private async Task ImportTransitionsAsync(
        AdministrativeTransitionDataset dataset,
        CancellationToken cancellationToken)
    {
        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
        var existing = await _dbContext.AdministrativeUnitTransitions.ToListAsync(cancellationToken);
        var byKey = existing.ToDictionary(item => (item.LegacyUnitCode, item.CurrentUnitCode));
        var incoming = dataset.Transitions
            .Select(item => (item.LegacyUnitCode, item.CurrentUnitCode))
            .ToHashSet();

        foreach (var item in dataset.Transitions)
        {
            if (byKey.TryGetValue((item.LegacyUnitCode, item.CurrentUnitCode), out var transition))
            {
                transition.Update(item.TransitionType, item.SourceDocument, item.SourceReference,
                    item.EffectiveDate, dataset.DatasetVersion, item.Note);
            }
            else
            {
                _dbContext.AdministrativeUnitTransitions.Add(new AdministrativeUnitTransition(
                    item.LegacyUnitCode, item.CurrentUnitCode, item.TransitionType,
                    item.SourceDocument, item.SourceReference, item.EffectiveDate,
                    dataset.DatasetVersion, item.Note));
            }
        }

        _dbContext.AdministrativeUnitTransitions.RemoveRange(existing.Where(item =>
            !incoming.Contains((item.LegacyUnitCode, item.CurrentUnitCode))));
        await _dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private static void ValidateDataset(AdministrativeUnitDataset dataset, string file)
    {
        if (!Enum.IsDefined(dataset.Scheme))
            throw new InvalidOperationException($"Dataset scheme is invalid: {file}");
        if (string.IsNullOrWhiteSpace(dataset.DatasetVersion)
            || string.IsNullOrWhiteSpace(dataset.SourceDocument)
            || string.IsNullOrWhiteSpace(dataset.SourceReference))
            throw new InvalidOperationException($"Dataset metadata is incomplete: {file}");
        if (dataset.Units.Count == 0)
            throw new InvalidOperationException($"Dataset has no units: {file}");

        foreach (var unit in dataset.Units)
        {
            if (string.IsNullOrWhiteSpace(unit.Code)
                || string.IsNullOrWhiteSpace(unit.Name)
                || string.IsNullOrWhiteSpace(unit.FullName))
                throw new InvalidOperationException($"Dataset has a unit with missing identity: {file}");
            var validHierarchy = dataset.Scheme switch
            {
                AdministrativeScheme.Current => unit.HierarchyLevel switch
                {
                    1 => unit.UnitType is AdministrativeUnitType.Province or AdministrativeUnitType.Municipality,
                    2 => unit.UnitType is AdministrativeUnitType.Commune
                        or AdministrativeUnitType.Ward
                        or AdministrativeUnitType.SpecialZone,
                    _ => false
                },
                AdministrativeScheme.Legacy => unit.HierarchyLevel switch
                {
                    1 => unit.UnitType is AdministrativeUnitType.Province or AdministrativeUnitType.Municipality,
                    2 => unit.UnitType is AdministrativeUnitType.District
                        or AdministrativeUnitType.UrbanDistrict
                        or AdministrativeUnitType.Town
                        or AdministrativeUnitType.ProvincialCity,
                    3 => unit.UnitType is AdministrativeUnitType.Commune
                        or AdministrativeUnitType.Ward
                        or AdministrativeUnitType.Township,
                    _ => false
                },
                _ => false
            };
            if (!validHierarchy)
                throw new InvalidOperationException($"Dataset has an invalid hierarchy at {unit.Code}: {file}");
            if (unit.HierarchyLevel == 1 && unit.ParentCode is not null)
                throw new InvalidOperationException($"Province {unit.Code} cannot have a parent: {file}");
            if (unit.HierarchyLevel > 1 && string.IsNullOrWhiteSpace(unit.ParentCode))
                throw new InvalidOperationException($"Unit {unit.Code} requires a parent: {file}");
        }
    }

    private sealed class AdministrativeUnitDataset
    {
        public string DatasetVersion { get; init; } = string.Empty;
        public AdministrativeScheme Scheme { get; init; }
        public DateOnly EffectiveFrom { get; init; }
        public DateOnly? EffectiveTo { get; init; }
        public string SourceDocument { get; init; } = string.Empty;
        public string SourceReference { get; init; } = string.Empty;
        public string SourceChecksum { get; init; } = string.Empty;
        public List<AdministrativeUnitImportRow> Units { get; init; } = [];
    }

    private sealed class AdministrativeUnitImportRow
    {
        public string Code { get; init; } = string.Empty;
        public string Name { get; init; } = string.Empty;
        public string FullName { get; init; } = string.Empty;
        public AdministrativeUnitType UnitType { get; init; }
        public int HierarchyLevel { get; init; }
        public string? ParentCode { get; init; }
        public bool IsActive { get; init; } = true;
        public int SortOrder { get; init; }
    }

    private sealed class AdministrativeTransitionDataset
    {
        public string DatasetVersion { get; init; } = "2025-07-01";
        public List<AdministrativeTransitionImportRow> Transitions { get; init; } = [];
    }

    private sealed class AdministrativeTransitionImportRow
    {
        public string LegacyUnitCode { get; init; } = string.Empty;
        public string CurrentUnitCode { get; init; } = string.Empty;
        public AdministrativeTransitionType TransitionType { get; init; }
        public string SourceDocument { get; init; } = string.Empty;
        public string SourceReference { get; init; } = string.Empty;
        public DateOnly EffectiveDate { get; init; }
        public string? Note { get; init; }
    }
}

public sealed class AdministrativeDataImportHostedService : IHostedService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly Microsoft.Extensions.Options.IOptions<AdministrativeDataOptions> _options;

    public AdministrativeDataImportHostedService(
        IServiceProvider serviceProvider,
        Microsoft.Extensions.Options.IOptions<AdministrativeDataOptions> options)
    {
        _serviceProvider = serviceProvider;
        _options = options;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!_options.Value.ImportOnStartup)
            return;
        await using var scope = _serviceProvider.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<IAdministrativeDataImporter>()
            .ImportAsync(cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
