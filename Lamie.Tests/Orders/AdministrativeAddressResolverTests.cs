using Lamie.API.Services;
using Lamie.Application.Addresses;
using Lamie.Domain.Entities;
using Xunit;

namespace Lamie.Tests.Orders;

public sealed class AdministrativeAddressResolverTests
{
    private static readonly AdministrativeAddressResolutionOptions Options = new()
    {
        DefaultProvinceCode = "79",
        CandidateLimit = 8,
        ConfidentThreshold = 0.82m
    };

    [Fact]
    public void Explicit_Dong_Thap_never_gets_overridden_by_default_Ho_Chi_Minh_City()
    {
        var result = Resolve("Cầu ba Tây, xã Thường Lạc, huyện Hồng Ngự, Đồng Tháp");

        Assert.NotNull(result.SelectedCandidate);
        Assert.Equal(AdministrativeScheme.Legacy, result.SelectedCandidate.Scheme);
        Assert.Equal("87", result.SelectedCandidate.ProvinceCode);
        Assert.Equal("870", result.SelectedCandidate.DistrictCode);
        Assert.Equal("29977", result.SelectedCandidate.CommuneCode);
        Assert.False(result.SelectedCandidate.UsedDefaultProvince);
        Assert.Equal("Cầu ba Tây", result.SelectedCandidate.AddressDetail);
    }

    [Fact]
    public void Explicit_province_without_district_still_keeps_Dong_Thap()
    {
        var result = Resolve("xã Thường Lạc, Đồng Tháp");

        Assert.NotNull(result.SelectedCandidate);
        Assert.Contains(result.SelectedCandidate.ProvinceCode, new[] { "82", "87" });
        Assert.NotEqual("79", result.SelectedCandidate.ProvinceCode);
        Assert.False(result.SelectedCandidate.UsedDefaultProvince);
    }

    [Fact]
    public void Missing_province_prioritizes_Ho_Chi_Minh_City_and_auto_selects_the_top_candidate()
    {
        var result = Resolve("80/3 Nguyễn Trãi, phường Chợ Quán");

        Assert.NotNull(result.SelectedCandidate);
        Assert.Equal("79", result.SelectedCandidate.ProvinceCode);
        Assert.Equal("27301", result.SelectedCandidate.CommuneCode);
        Assert.True(result.SelectedCandidate.UsedDefaultProvince);
        Assert.Same(result.Candidates[0], result.SelectedCandidate);
        Assert.Contains(result.Warnings, warning => warning.Contains("đã ưu tiên Thành phố Hồ Chí Minh", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("461 Phan Văn Trị, TP HCM")]
    [InlineData("461 Phan Văn Trị, TP.HCM")]
    [InlineData("461 Phan Văn Trị, HCM")]
    [InlineData("461 Phan Văn Trị, Ho Chi Minh")]
    [InlineData("461 Phan Văn Trị, thành phố hồ chí minh")]
    [InlineData("461 Phan Văn Trị,   TP   HCM")]
    public void Ho_Chi_Minh_City_aliases_resolve_to_the_same_province(string text)
    {
        var result = Resolve(text);

        Assert.NotNull(result.SelectedCandidate);
        Assert.Equal("79", result.SelectedCandidate.ProvinceCode);
        Assert.False(result.SelectedCandidate.UsedDefaultProvince);
        Assert.Equal("461 Phan Văn Trị", result.SelectedCandidate.AddressDetail);
    }

    [Fact]
    public void No_accent_input_matches_the_correct_current_ward()
    {
        var result = Resolve("461 Phan Van Tri, phuong An Nhon");

        Assert.NotNull(result.SelectedCandidate);
        Assert.Equal(AdministrativeScheme.Current, result.SelectedCandidate.Scheme);
        Assert.Equal("79", result.SelectedCandidate.ProvinceCode);
        Assert.Equal("26876", result.SelectedCandidate.CommuneCode);
    }

    [Fact]
    public void Fuzzy_match_ranks_below_an_exact_normalized_match()
    {
        var exact = Resolve("80/3 Nguyễn Trãi, phường Chợ Quán");
        var fuzzy = Resolve("80/3 Nguyễn Trãi, phường Chợ Qán");

        Assert.NotNull(fuzzy.SelectedCandidate);
        Assert.Equal("27301", fuzzy.SelectedCandidate.CommuneCode);
        Assert.True(fuzzy.Confidence < exact.Confidence);
        Assert.False(fuzzy.IsConfident);
    }

    [Fact]
    public void Explicit_legacy_district_makes_the_legacy_hierarchy_rank_first()
    {
        var result = Resolve("xã Thường Lạc, huyện Hồng Ngự, Đồng Tháp");

        Assert.NotNull(result.SelectedCandidate);
        Assert.Equal(AdministrativeScheme.Legacy, result.SelectedCandidate.Scheme);
        Assert.Equal("Huyện Hồng Ngự", result.SelectedCandidate.DistrictName);
        Assert.True(result.IsConfident);
    }

    [Fact]
    public void District_abbreviation_prefers_the_legacy_district_candidate()
    {
        var result = Resolve("12 Nguyễn Văn Bảo, Q. Gò Vấp");

        Assert.NotNull(result.SelectedCandidate);
        Assert.Equal(AdministrativeScheme.Legacy, result.SelectedCandidate.Scheme);
        Assert.Equal("764", result.SelectedCandidate.DistrictCode);
        Assert.Equal("12 Nguyễn Văn Bảo", result.SelectedCandidate.AddressDetail);
    }

    [Fact]
    public void More_specific_current_ward_wins_for_Thu_Duc_without_a_legacy_prefix()
    {
        var result = Resolve("123 Lê Văn Việt, Thủ Đức");

        Assert.NotNull(result.SelectedCandidate);
        Assert.Equal(AdministrativeScheme.Current, result.SelectedCandidate.Scheme);
        Assert.Equal("26824", result.SelectedCandidate.CommuneCode);
    }

    [Fact]
    public void Ambiguous_commune_still_auto_selects_the_highest_ranked_candidate()
    {
        var result = Resolve("Phường An Nhơn");

        Assert.True(result.Candidates.Count > 1);
        Assert.NotNull(result.SelectedCandidate);
        Assert.Equal(result.Candidates.Max(candidate => candidate.Confidence), result.SelectedCandidate.Confidence);
        Assert.Equal("79", result.SelectedCandidate.ProvinceCode);
        Assert.Contains(result.Warnings, warning => warning.Contains("tự chọn kết quả có điểm cao nhất", StringComparison.Ordinal));
    }

    [Fact]
    public void Low_confidence_street_only_input_uses_default_province_without_fake_confidence()
    {
        var result = Resolve("Nguyễn Trãi");

        Assert.NotNull(result.SelectedCandidate);
        Assert.Equal("79", result.SelectedCandidate.ProvinceCode);
        Assert.True(result.SelectedCandidate.UsedDefaultProvince);
        Assert.False(result.IsConfident);
        Assert.InRange(result.Confidence, 0.01m, 0.81m);
        Assert.Contains(result.Warnings, warning => warning.Contains("độ tin cậy thấp", StringComparison.Ordinal));
    }

    [Fact]
    public void No_result_preserves_raw_text_and_does_not_throw()
    {
        var result = Resolve("khongro");

        Assert.Equal("khongro", result.OriginalText);
        Assert.Null(result.SelectedCandidate);
        Assert.Empty(result.Candidates);
        Assert.Contains(result.Warnings, warning => warning.Contains("Không xác định được", StringComparison.Ordinal));
    }

    [Fact]
    public void Special_delivery_instructions_remain_in_address_detail()
    {
        var result = Resolve("461 Phan Văn Trị, hẻm cạnh số 25, cổng màu xanh, Phường An Nhơn");

        Assert.NotNull(result.SelectedCandidate);
        Assert.Equal("461 Phan Văn Trị, hẻm cạnh số 25, cổng màu xanh", result.SelectedCandidate.AddressDetail);
        Assert.Equal("461 Phan Văn Trị, hẻm cạnh số 25, cổng màu xanh, Phường An Nhơn, Thành phố Hồ Chí Minh", result.SelectedCandidate.FullAddress);
    }

    private static AddressResolutionDto Resolve(string text) => AdministrativeAddressResolver.Resolve(
        new ResolveAddressRequest { Text = text },
        Units(),
        Options);

    private static IReadOnlyCollection<AdministrativeUnit> Units() =>
    [
        Unit("79", "Hồ Chí Minh", "Thành phố Hồ Chí Minh", AdministrativeScheme.Current, AdministrativeUnitType.Municipality, 1),
        Unit("52", "Gia Lai", "Tỉnh Gia Lai", AdministrativeScheme.Current, AdministrativeUnitType.Province, 1),
        Unit("82", "Đồng Tháp", "Tỉnh Đồng Tháp", AdministrativeScheme.Current, AdministrativeUnitType.Province, 1),
        Unit("26876", "An Nhơn", "Phường An Nhơn", AdministrativeScheme.Current, AdministrativeUnitType.Ward, 2, "79"),
        Unit("21910", "An Nhơn", "Phường An Nhơn", AdministrativeScheme.Current, AdministrativeUnitType.Ward, 2, "52"),
        Unit("27301", "Chợ Quán", "Phường Chợ Quán", AdministrativeScheme.Current, AdministrativeUnitType.Ward, 2, "79"),
        Unit("26884", "Gò Vấp", "Phường Gò Vấp", AdministrativeScheme.Current, AdministrativeUnitType.Ward, 2, "79"),
        Unit("26824", "Thủ Đức", "Phường Thủ Đức", AdministrativeScheme.Current, AdministrativeUnitType.Ward, 2, "79"),
        Unit("29978", "Thường Lạc", "Phường Thường Lạc", AdministrativeScheme.Current, AdministrativeUnitType.Ward, 2, "82"),
        Unit("79", "Hồ Chí Minh", "Thành phố Hồ Chí Minh", AdministrativeScheme.Legacy, AdministrativeUnitType.Municipality, 1),
        Unit("87", "Đồng Tháp", "Tỉnh Đồng Tháp", AdministrativeScheme.Legacy, AdministrativeUnitType.Province, 1),
        Unit("870", "Hồng Ngự", "Huyện Hồng Ngự", AdministrativeScheme.Legacy, AdministrativeUnitType.District, 2, "87"),
        Unit("764", "Gò Vấp", "Quận Gò Vấp", AdministrativeScheme.Legacy, AdministrativeUnitType.UrbanDistrict, 2, "79"),
        Unit("769", "Thủ Đức", "Thành phố Thủ Đức", AdministrativeScheme.Legacy, AdministrativeUnitType.ProvincialCity, 2, "79"),
        Unit("29977", "Thường Lạc", "Xã Thường Lạc", AdministrativeScheme.Legacy, AdministrativeUnitType.Commune, 3, "870")
    ];

    private static AdministrativeUnit Unit(
        string code,
        string name,
        string fullName,
        AdministrativeScheme scheme,
        AdministrativeUnitType unitType,
        int hierarchyLevel,
        string? parentCode = null) => new(
        code,
        name,
        fullName,
        VietnameseTextNormalizer.Search(name),
        scheme,
        unitType,
        hierarchyLevel,
        parentCode,
        new DateOnly(2025, 1, 1),
        null,
        true,
        "test",
        "https://example.test",
        "test",
        1);
}
