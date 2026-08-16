using Lamie.Application.Addresses;
using Lamie.Domain.Entities;

namespace Lamie.API.Services;

/// <summary>
/// Deterministically ranks administrative address candidates. It does not use geographic
/// coordinates and always returns the highest-scoring candidate when at least one exists.
/// </summary>
public static class AdministrativeAddressResolver
{
    private const decimal DefaultProvinceConfidenceCap = 0.81m;

    public static AddressResolutionDto Resolve(
        ResolveAddressRequest request,
        IReadOnlyCollection<AdministrativeUnit> sourceUnits,
        AdministrativeAddressResolutionOptions options)
    {
        var originalText = NormalizeOriginalText(request.Text);
        var normalizedText = VietnameseTextNormalizer.Search(originalText);
        var units = sourceUnits.Where(unit => unit.IsActive).ToList();
        var unitByKey = units.ToDictionary(unit => (unit.Scheme, unit.Code));
        var provinces = units.Where(unit => unit.HierarchyLevel == 1).ToList();
        var defaultProvinceCode = string.IsNullOrWhiteSpace(request.DefaultProvinceCode)
            ? options.DefaultProvinceCode.Trim()
            : request.DefaultProvinceCode.Trim();
        var segments = SplitSegments(originalText);
        var explicitProvinceCodes = FindExplicitProvinceCodes(segments, provinces, defaultProvinceCode);
        var hasExplicitProvince = explicitProvinceCodes.Count > 0;

        var matches = units
            .Where(unit => unit.HierarchyLevel > 1)
            .Select(unit => new UnitMatch(unit, MatchUnit(normalizedText, segments, unit)))
            .Where(match => match.Strength > 0)
            .ToList();
        var matchedDistricts = matches
            .Where(match => match.Unit.Scheme == AdministrativeScheme.Legacy && match.Unit.HierarchyLevel == 2)
            .ToDictionary(match => match.Unit.Code);
        var communeNameCounts = units
            .Where(unit => unit.UnitType is AdministrativeUnitType.Commune
                or AdministrativeUnitType.Ward
                or AdministrativeUnitType.Township
                or AdministrativeUnitType.SpecialZone)
            .GroupBy(unit => (unit.Scheme, BareName(unit)))
            .ToDictionary(group => group.Key, group => group.Count());

        var ranked = new List<RankedCandidate>();
        foreach (var match in matches.Where(item => IsCommuneLevel(item.Unit)))
        {
            var candidate = BuildCommuneCandidate(
                originalText,
                match,
                unitByKey,
                matchedDistricts,
                communeNameCounts,
                explicitProvinceCodes,
                hasExplicitProvince,
                defaultProvinceCode,
                request.PreferredScheme);
            if (candidate is not null)
                ranked.Add(candidate);
        }

        foreach (var match in matchedDistricts.Values)
        {
            var candidate = BuildDistrictCandidate(
                originalText,
                match,
                unitByKey,
                explicitProvinceCodes,
                hasExplicitProvince,
                defaultProvinceCode,
                request.PreferredScheme);
            if (candidate is not null)
                ranked.Add(candidate);
        }

        if (ranked.Count == 0 && hasExplicitProvince)
        {
            foreach (var province in provinces.Where(province =>
                         explicitProvinceCodes.TryGetValue(province.Scheme, out var codes)
                         && codes.Contains(province.Code)))
            {
                var detail = ExtractDetail(originalText, null, null, province, defaultProvinceCode);
                var score = 0.63m + PreferredSchemeBoost(request.PreferredScheme, province.Scheme);
                ranked.Add(new RankedCandidate(
                    CreateCandidate(province.Scheme, province, null, null, detail, score,
                        "Đã nhận diện rõ tỉnh/thành phố trong nội dung dán.", false),
                    Specificity: 1));
            }
        }

        if (ranked.Count == 0 && !hasExplicitProvince && LooksLikeAddress(originalText, normalizedText))
        {
            var defaultScheme = request.PreferredScheme ?? AdministrativeScheme.Current;
            var province = provinces.FirstOrDefault(unit =>
                    unit.Scheme == defaultScheme && unit.Code == defaultProvinceCode)
                ?? provinces.FirstOrDefault(unit =>
                    unit.Scheme == AdministrativeScheme.Current && unit.Code == defaultProvinceCode)
                ?? provinces.FirstOrDefault(unit => unit.Code == defaultProvinceCode);
            if (province is not null)
            {
                ranked.Add(new RankedCandidate(
                    CreateCandidate(province.Scheme, province, null, null, originalText, 0.30m,
                        "Chưa nhận diện được đơn vị cấp dưới; đã ưu tiên tỉnh/thành mặc định.", true),
                    Specificity: 1));
            }
        }

        var candidates = ranked
            .GroupBy(item => CandidateKey(item.Candidate))
            .Select(group => group.OrderByDescending(item => item.Candidate.Confidence).First())
            .OrderByDescending(item => item.Candidate.Confidence)
            .ThenByDescending(item => item.Specificity)
            .ThenBy(item => request.PreferredScheme.HasValue && item.Candidate.Scheme == request.PreferredScheme ? 0 : 1)
            .ThenBy(item => item.Candidate.Scheme == AdministrativeScheme.Current ? 0 : 1)
            .ThenBy(item => item.Candidate.FullAddress, StringComparer.Ordinal)
            .Take(Math.Clamp(options.CandidateLimit, 1, 20))
            .Select(item => item.Candidate)
            .ToList();
        var selected = candidates.FirstOrDefault();
        var isConfident = selected is not null
            && selected.Confidence >= options.ConfidentThreshold
            && (candidates.Count < 2 || selected.Confidence - candidates[1].Confidence >= 0.04m);
        var warnings = BuildWarnings(selected, candidates, isConfident, hasExplicitProvince);

        return new AddressResolutionDto(
            originalText,
            normalizedText,
            isConfident,
            selected?.Confidence ?? 0,
            selected,
            candidates,
            warnings);
    }

    private static RankedCandidate? BuildCommuneCandidate(
        string originalText,
        UnitMatch match,
        IReadOnlyDictionary<(AdministrativeScheme Scheme, string Code), AdministrativeUnit> unitByKey,
        IReadOnlyDictionary<string, UnitMatch> matchedDistricts,
        IReadOnlyDictionary<(AdministrativeScheme Scheme, string Name), int> communeNameCounts,
        IReadOnlyDictionary<AdministrativeScheme, HashSet<string>> explicitProvinceCodes,
        bool hasExplicitProvince,
        string defaultProvinceCode,
        AdministrativeScheme? preferredScheme)
    {
        AdministrativeUnit? district = null;
        AdministrativeUnit? province = null;
        if (match.Unit.Scheme == AdministrativeScheme.Current)
        {
            if (match.Unit.ParentCode is not null)
                unitByKey.TryGetValue((match.Unit.Scheme, match.Unit.ParentCode), out province);
        }
        else if (match.Unit.ParentCode is not null
                 && unitByKey.TryGetValue((match.Unit.Scheme, match.Unit.ParentCode), out district)
                 && district.ParentCode is not null)
        {
            unitByKey.TryGetValue((match.Unit.Scheme, district.ParentCode), out province);
        }

        if (province is null || !ProvinceIsAllowed(province, explicitProvinceCodes, hasExplicitProvince))
            return null;

        var usedDefault = !hasExplicitProvince && province.Code == defaultProvinceCode;
        var score = match.Strength;
        if (hasExplicitProvince)
            score += 0.23m;
        if (district is not null && matchedDistricts.TryGetValue(district.Code, out var districtMatch))
            score += 0.15m + Math.Max(0, districtMatch.Strength - 0.60m) / 2;
        if (communeNameCounts.TryGetValue((match.Unit.Scheme, BareName(match.Unit)), out var count) && count == 1)
            score += 0.06m;
        if (usedDefault)
            score += 0.07m;
        score += PreferredSchemeBoost(preferredScheme, match.Unit.Scheme);
        score = ClampScore(score, usedDefault);

        var detail = ExtractDetail(originalText, match.Unit, district, province, defaultProvinceCode);
        var reason = hasExplicitProvince && district is not null && matchedDistricts.ContainsKey(district.Code)
            ? "Tên xã/phường, cấp huyện và tỉnh/thành cùng khớp trong một hierarchy."
            : hasExplicitProvince
                ? "Tên đơn vị cấp xã và tỉnh/thành cùng khớp."
                : usedDefault
                    ? $"Tên đơn vị cấp xã khớp; {province.FullName} được dùng làm ưu tiên mặc định."
                    : "Tên đơn vị cấp xã khớp nhưng văn bản không ghi rõ tỉnh/thành.";
        return new RankedCandidate(
            CreateCandidate(match.Unit.Scheme, province, district, match.Unit, detail, score, reason, usedDefault),
            Specificity: 3);
    }

    private static RankedCandidate? BuildDistrictCandidate(
        string originalText,
        UnitMatch match,
        IReadOnlyDictionary<(AdministrativeScheme Scheme, string Code), AdministrativeUnit> unitByKey,
        IReadOnlyDictionary<AdministrativeScheme, HashSet<string>> explicitProvinceCodes,
        bool hasExplicitProvince,
        string defaultProvinceCode,
        AdministrativeScheme? preferredScheme)
    {
        if (match.Unit.ParentCode is null
            || !unitByKey.TryGetValue((AdministrativeScheme.Legacy, match.Unit.ParentCode), out var province)
            || !ProvinceIsAllowed(province, explicitProvinceCodes, hasExplicitProvince))
            return null;

        var usedDefault = !hasExplicitProvince && province.Code == defaultProvinceCode;
        var score = match.Strength + (hasExplicitProvince ? 0.23m : 0) + (usedDefault ? 0.07m : 0);
        score += PreferredSchemeBoost(preferredScheme, AdministrativeScheme.Legacy);
        score = ClampScore(score, usedDefault);
        var detail = ExtractDetail(originalText, null, match.Unit, province, defaultProvinceCode);
        return new RankedCandidate(
            CreateCandidate(
                AdministrativeScheme.Legacy,
                province,
                match.Unit,
                null,
                detail,
                score,
                hasExplicitProvince
                    ? "Tên đơn vị cấp huyện và tỉnh/thành cùng khớp; chưa có xã/phường."
                    : usedDefault
                        ? $"Tên đơn vị cấp huyện khớp; {province.FullName} được dùng làm ưu tiên mặc định."
                        : "Tên đơn vị cấp huyện khớp; chưa có xã/phường hoặc tỉnh/thành.",
                usedDefault),
            Specificity: 2);
    }

    private static AddressResolutionSuggestionDto CreateCandidate(
        AdministrativeScheme scheme,
        AdministrativeUnit province,
        AdministrativeUnit? district,
        AdministrativeUnit? commune,
        string? detail,
        decimal confidence,
        string reason,
        bool usedDefaultProvince)
    {
        var fullAddress = string.Join(", ", new[]
        {
            detail,
            commune?.FullName,
            district?.FullName,
            province.FullName
        }.Where(value => !string.IsNullOrWhiteSpace(value)));
        return new AddressResolutionSuggestionDto(
            scheme,
            province.Code,
            province.FullName,
            district?.Code,
            district?.FullName,
            commune?.Code,
            commune?.FullName,
            detail,
            fullAddress,
            confidence,
            reason,
            usedDefaultProvince);
    }

    private static decimal MatchUnit(
        string normalizedInput,
        IReadOnlyList<string> segments,
        AdministrativeUnit unit)
    {
        var bare = BareName(unit);
        var full = VietnameseTextNormalizer.Search(unit.FullName);
        var best = 0m;
        foreach (var segment in segments)
        {
            var parsed = ParseSegment(segment);
            var compatible = parsed.ExpectedTypes.Count == 0 || parsed.ExpectedTypes.Contains(unit.UnitType);
            if (segment == full)
                best = Math.Max(best, 0.70m);
            if (compatible && parsed.Remainder == bare)
                best = Math.Max(best, parsed.ExpectedTypes.Count > 0 ? 0.69m : 0.64m);
            if (parsed.ExpectedTypes.Count == 0 && segment == bare)
                best = Math.Max(best, 0.64m);
            if (compatible && ContainsTerm(segment, full))
                best = Math.Max(best, 0.58m);
            if (compatible && bare.Length >= 4 && ContainsTerm(segment, bare))
                best = Math.Max(best, 0.50m);
            if (compatible && bare.Length >= 4 && parsed.Remainder.Length is >= 4 and <= 80)
            {
                var similarity = Similarity(parsed.Remainder, bare);
                if (similarity >= 0.84m)
                    best = Math.Max(best, 0.34m + similarity * 0.18m);
            }
        }

        if (best == 0 && ContainsTerm(normalizedInput, full))
            best = 0.56m;
        return best;
    }

    private static Dictionary<AdministrativeScheme, HashSet<string>> FindExplicitProvinceCodes(
        IReadOnlyList<string> segments,
        IReadOnlyCollection<AdministrativeUnit> provinces,
        string defaultProvinceCode)
    {
        var result = new Dictionary<AdministrativeScheme, HashSet<string>>();
        foreach (var province in provinces)
        {
            var aliases = ProvinceAliases(province, defaultProvinceCode);
            var matched = segments.Any(segment => aliases.Any(alias =>
                segment == alias || (alias.Length >= 4 && ContainsTerm(segment, alias))));
            if (!matched)
                continue;
            if (!result.TryGetValue(province.Scheme, out var codes))
            {
                codes = [];
                result[province.Scheme] = codes;
            }
            codes.Add(province.Code);
        }
        return result;
    }

    private static IReadOnlySet<string> ProvinceAliases(AdministrativeUnit province, string defaultProvinceCode)
    {
        var bare = BareName(province);
        var aliases = new HashSet<string>(StringComparer.Ordinal)
        {
            bare,
            VietnameseTextNormalizer.Search(province.FullName),
            $"tinh {bare}",
            $"thanh pho {bare}",
            $"tp {bare}"
        };
        if (province.Code == defaultProvinceCode)
        {
            aliases.UnionWith([
                "hcm",
                "tp hcm",
                "tphcm",
                "ho chi minh",
                "tp ho chi minh",
                "thanh pho hcm",
                "thanh pho ho chi minh",
                "sai gon"
            ]);
        }
        return aliases;
    }

    private static string? ExtractDetail(
        string originalText,
        AdministrativeUnit? commune,
        AdministrativeUnit? district,
        AdministrativeUnit? province,
        string defaultProvinceCode)
    {
        var administrativeAliases = new HashSet<string>(StringComparer.Ordinal);
        foreach (var unit in new[] { commune, district }.Where(unit => unit is not null))
        {
            administrativeAliases.Add(BareName(unit!));
            administrativeAliases.Add(VietnameseTextNormalizer.Search(unit!.FullName));
        }
        if (province is not null)
            administrativeAliases.UnionWith(ProvinceAliases(province, defaultProvinceCode));

        var detailParts = originalText
            .Split([',', ';', '\n'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Where(part =>
            {
                var normalized = VietnameseTextNormalizer.Search(part);
                var parsed = ParseSegment(normalized);
                return !administrativeAliases.Contains(normalized)
                    && !administrativeAliases.Contains(parsed.Remainder);
            });
        var result = string.Join(", ", detailParts).Trim(' ', ',');
        return string.IsNullOrWhiteSpace(result) ? null : result;
    }

    private static IReadOnlyList<string> BuildWarnings(
        AddressResolutionSuggestionDto? selected,
        IReadOnlyList<AddressResolutionSuggestionDto> candidates,
        bool isConfident,
        bool hasExplicitProvince)
    {
        if (selected is null)
            return ["Không xác định được đơn vị hành chính. Vui lòng kiểm tra lại."];

        var warnings = new List<string>();
        if (selected.UsedDefaultProvince && !hasExplicitProvince)
            warnings.Add($"Không tìm thấy tỉnh/thành trong địa chỉ, đã ưu tiên {selected.ProvinceName}.");
        if (!isConfident)
            warnings.Add("Địa chỉ được nhận diện với độ tin cậy thấp. Vui lòng kiểm tra lại.");
        if (candidates.Count > 1 && selected.Confidence - candidates[1].Confidence < 0.08m)
            warnings.Add("Có nhiều địa chỉ tương tự; hệ thống đã tự chọn kết quả có điểm cao nhất.");
        if (selected.CommuneCode is null)
            warnings.Add("Chưa nhận diện được xã/phường; vui lòng bổ sung nếu cần địa chỉ hành chính đầy đủ.");
        return warnings.Distinct(StringComparer.Ordinal).ToList();
    }

    private static bool ProvinceIsAllowed(
        AdministrativeUnit province,
        IReadOnlyDictionary<AdministrativeScheme, HashSet<string>> explicitProvinceCodes,
        bool hasExplicitProvince) =>
        !hasExplicitProvince
        || (explicitProvinceCodes.TryGetValue(province.Scheme, out var codes) && codes.Contains(province.Code));

    private static decimal ClampScore(decimal score, bool usedDefaultProvince) =>
        Math.Min(usedDefaultProvince ? DefaultProvinceConfidenceCap : 0.99m, score);

    private static decimal PreferredSchemeBoost(AdministrativeScheme? preferred, AdministrativeScheme candidate) =>
        preferred == candidate ? 0.015m : 0;

    private static bool IsCommuneLevel(AdministrativeUnit unit) =>
        unit.Scheme == AdministrativeScheme.Current && unit.HierarchyLevel == 2
        || unit.Scheme == AdministrativeScheme.Legacy && unit.HierarchyLevel == 3;

    private static string CandidateKey(AddressResolutionSuggestionDto candidate) =>
        $"{candidate.Scheme}:{candidate.ProvinceCode}:{candidate.DistrictCode}:{candidate.CommuneCode}";

    private static string BareName(AdministrativeUnit unit) => VietnameseTextNormalizer.Search(unit.Name);

    private static string NormalizeOriginalText(string value) => VietnameseTextNormalizer.Display(
        value.Replace("\r\n", ", ", StringComparison.Ordinal).Replace('\n', ',').Replace('\r', ','));

    private static IReadOnlyList<string> SplitSegments(string originalText) => originalText
        .Split([',', ';', '\n'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
        .Select(VietnameseTextNormalizer.Search)
        .Where(segment => segment.Length > 0)
        .ToList();

    private static ParsedSegment ParseSegment(string normalizedSegment)
    {
        var prefixes = new (string Prefix, AdministrativeUnitType[] Types)[]
        {
            ("thanh pho", [AdministrativeUnitType.Municipality, AdministrativeUnitType.ProvincialCity]),
            ("thi tran", [AdministrativeUnitType.Township]),
            ("thi xa", [AdministrativeUnitType.Town]),
            ("dac khu", [AdministrativeUnitType.SpecialZone]),
            ("phuong", [AdministrativeUnitType.Ward]),
            ("huyen", [AdministrativeUnitType.District]),
            ("quan", [AdministrativeUnitType.UrbanDistrict]),
            ("tinh", [AdministrativeUnitType.Province]),
            ("tp", [AdministrativeUnitType.Municipality, AdministrativeUnitType.ProvincialCity]),
            ("tx", [AdministrativeUnitType.Town]),
            ("tt", [AdministrativeUnitType.Township]),
            ("p", [AdministrativeUnitType.Ward]),
            ("x", [AdministrativeUnitType.Commune]),
            ("h", [AdministrativeUnitType.District]),
            ("q", [AdministrativeUnitType.UrbanDistrict])
        };
        foreach (var (prefix, types) in prefixes)
        {
            if (normalizedSegment.StartsWith($"{prefix} ", StringComparison.Ordinal))
                return new ParsedSegment(normalizedSegment[(prefix.Length + 1)..].Trim(), types.ToHashSet());
        }
        return new ParsedSegment(normalizedSegment, []);
    }

    private static bool ContainsTerm(string input, string term) =>
        term.Length >= 2 && $" {input} ".Contains($" {term} ", StringComparison.Ordinal);

    private static bool LooksLikeAddress(string originalText, string normalizedText)
    {
        if (normalizedText.Length == 0)
            return false;
        var tokenCount = normalizedText.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
        return tokenCount >= 2 || originalText.Any(char.IsDigit)
            || new[] { "duong", "hem", "ngo", "so", "ap", "thon" }.Any(token => ContainsTerm(normalizedText, token));
    }

    private static decimal Similarity(string left, string right)
    {
        if (left == right)
            return 1;
        var previous = Enumerable.Range(0, right.Length + 1).ToArray();
        var current = new int[right.Length + 1];
        for (var i = 1; i <= left.Length; i++)
        {
            current[0] = i;
            for (var j = 1; j <= right.Length; j++)
            {
                var cost = left[i - 1] == right[j - 1] ? 0 : 1;
                current[j] = Math.Min(Math.Min(current[j - 1] + 1, previous[j] + 1), previous[j - 1] + cost);
            }
            (previous, current) = (current, previous);
        }
        return 1m - (decimal)previous[right.Length] / Math.Max(left.Length, right.Length);
    }

    private sealed record UnitMatch(AdministrativeUnit Unit, decimal Strength);
    private sealed record RankedCandidate(AddressResolutionSuggestionDto Candidate, int Specificity);
    private sealed record ParsedSegment(string Remainder, HashSet<AdministrativeUnitType> ExpectedTypes);
}
