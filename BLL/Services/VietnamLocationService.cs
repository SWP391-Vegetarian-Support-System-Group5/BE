using BLL.DTOs;

namespace BLL.Services;

/// <summary>
/// Provides the post-2025 two-tier Vietnamese administrative catalogue used by the map.
/// It is deliberately independent from EF Core until the team finalises the location schema.
/// </summary>
public sealed class VietnamLocationService : ILocationService
{
    private static readonly ProvinceResponse[] Provinces =
    [
        new("hanoi", "Hà Nội", "Thành phố", 21.0278m, 105.8342m),
        new("hue", "Huế", "Thành phố", 16.4637m, 107.5909m),
        new("haiphong", "Hải Phòng", "Thành phố", 20.8449m, 106.6881m),
        new("danang", "Đà Nẵng", "Thành phố", 16.0544m, 108.2022m),
        new("cantho", "Cần Thơ", "Thành phố", 10.0452m, 105.7469m),
        new("hochiminh", "Hồ Chí Minh", "Thành phố", 10.8231m, 106.6297m),
        new("laocai", "Lào Cai", "Tỉnh", 22.4809m, 103.9755m),
        new("tuyenquang", "Tuyên Quang", "Tỉnh", 21.7767m, 105.2280m),
        new("caobang", "Cao Bằng", "Tỉnh", 22.6666m, 106.2640m),
        new("laichau", "Lai Châu", "Tỉnh", 22.3862m, 103.4703m),
        new("dienbien", "Điện Biên", "Tỉnh", 21.3860m, 103.0230m),
        new("sonla", "Sơn La", "Tỉnh", 21.1022m, 103.7289m),
        new("langson", "Lạng Sơn", "Tỉnh", 21.8537m, 106.7615m),
        new("quangninh", "Quảng Ninh", "Tỉnh", 21.0064m, 107.2925m),
        new("thanhhoa", "Thanh Hóa", "Tỉnh", 19.8067m, 105.7852m),
        new("nghean", "Nghệ An", "Tỉnh", 19.2342m, 104.9200m),
        new("hatinh", "Hà Tĩnh", "Tỉnh", 18.3559m, 105.8877m),
        new("quangtri", "Quảng Trị", "Tỉnh", 16.7403m, 107.1855m),
        new("quangngai", "Quảng Ngãi", "Tỉnh", 15.1214m, 108.8044m),
        new("gialai", "Gia Lai", "Tỉnh", 13.8079m, 108.1094m),
        new("khanhhoa", "Khánh Hòa", "Tỉnh", 12.2585m, 109.0526m),
        new("lamdong", "Lâm Đồng", "Tỉnh", 11.5753m, 108.1429m),
        new("daklak", "Đắk Lắk", "Tỉnh", 12.7100m, 108.2378m),
        new("dongnai", "Đồng Nai", "Tỉnh", 11.0686m, 107.1676m),
        new("tayninh", "Tây Ninh", "Tỉnh", 11.3352m, 106.1099m),
        new("dongthap", "Đồng Tháp", "Tỉnh", 10.4938m, 105.6882m),
        new("vinhlong", "Vĩnh Long", "Tỉnh", 10.2396m, 105.9572m),
        new("angiang", "An Giang", "Tỉnh", 10.5216m, 105.1259m),
        new("camau", "Cà Mau", "Tỉnh", 9.1769m, 105.1524m),
        new("bacninh", "Bắc Ninh", "Tỉnh", 21.1861m, 106.0763m),
        new("phutho", "Phú Thọ", "Tỉnh", 21.3227m, 105.4019m),
        new("thainguyen", "Thái Nguyên", "Tỉnh", 21.5672m, 105.8252m),
        new("hungyen", "Hưng Yên", "Tỉnh", 20.8526m, 106.0169m),
        new("ninhbinh", "Ninh Bình", "Tỉnh", 20.2506m, 105.9745m)
    ];

    private static readonly IReadOnlyDictionary<string, AreaResponse[]> Areas =
        new Dictionary<string, AreaResponse[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["hochiminh"] =
            [
                new("all", "Tất cả khu vực", "Khu vực", 10.8231m, 106.6297m),
                new("thu-duc", "Phường Thủ Đức", "Phường", 10.8496m, 106.7537m),
                new("linh-xuan", "Phường Linh Xuân", "Phường", 10.8782m, 106.7720m),
                new("tam-binh", "Phường Tam Bình", "Phường", 10.8664m, 106.7373m),
                new("tang-nhon-phu", "Phường Tăng Nhơn Phú", "Phường", 10.8449m, 106.7812m),
                new("sai-gon", "Phường Sài Gòn", "Phường", 10.7781m, 106.6978m),
                new("ben-thanh", "Phường Bến Thành", "Phường", 10.7725m, 106.6940m),
                new("xuan-hoa", "Phường Xuân Hòa", "Phường", 10.7869m, 106.6847m),
                new("ban-co", "Phường Bàn Cờ", "Phường", 10.7758m, 106.6814m),
                new("phu-nhuan", "Phường Phú Nhuận", "Phường", 10.7991m, 106.6797m),
                new("tan-son-hoa", "Phường Tân Sơn Hòa", "Phường", 10.8038m, 106.6656m),
                new("cho-lon", "Phường Chợ Lớn", "Phường", 10.7542m, 106.6635m),
                new("an-dong", "Phường An Đông", "Phường", 10.7588m, 106.6751m)
            ],
            ["hanoi"] =
            [
                new("all", "Tất cả khu vực", "Khu vực", 21.0278m, 105.8342m),
                new("hoan-kiem", "Phường Hoàn Kiếm", "Phường", 21.0288m, 105.8522m),
                new("ba-dinh", "Phường Ba Đình", "Phường", 21.0358m, 105.8282m),
                new("cua-nam", "Phường Cửa Nam", "Phường", 21.0245m, 105.8442m),
                new("hai-ba-trung", "Phường Hai Bà Trưng", "Phường", 21.0070m, 105.8495m),
                new("tay-ho", "Phường Tây Hồ", "Phường", 21.0694m, 105.8181m),
                new("cau-giay", "Phường Cầu Giấy", "Phường", 21.0362m, 105.7906m)
            ],
            ["danang"] =
            [
                new("all", "Tất cả khu vực", "Khu vực", 16.0544m, 108.2022m),
                new("hai-chau", "Phường Hải Châu", "Phường", 16.0608m, 108.2210m),
                new("son-tra", "Phường Sơn Trà", "Phường", 16.1060m, 108.2520m),
                new("ngu-hanh-son", "Phường Ngũ Hành Sơn", "Phường", 16.0489m, 108.2422m),
                new("an-hai", "Phường An Hải", "Phường", 16.0678m, 108.2348m)
            ]
        };

    public IReadOnlyCollection<ProvinceResponse> GetProvinces() => Provinces;

    public IReadOnlyCollection<AreaResponse>? GetAreas(string provinceCode)
    {
        var province = Provinces.FirstOrDefault(item => item.Code.Equals(provinceCode, StringComparison.OrdinalIgnoreCase));
        if (province is null) return null;

        // Provinces not curated yet still expose a stable whole-province option for geocoding.
        return Areas.TryGetValue(provinceCode, out var areas)
            ? areas
            : [new AreaResponse("all", $"Toàn {province.Name}", "Khu vực", province.Latitude, province.Longitude)];
    }
}
