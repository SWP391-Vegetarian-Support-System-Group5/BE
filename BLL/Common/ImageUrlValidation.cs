namespace BLL.Common;

public static class ImageUrlValidation
{
    public static string? Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var url = value.Trim();
        if (url.Length > 1000 || !Uri.TryCreate(url, UriKind.Absolute, out var parsed) ||
            parsed.Scheme != Uri.UriSchemeHttps ||
            !parsed.Host.Equals("res.cloudinary.com", StringComparison.OrdinalIgnoreCase) ||
            !System.Text.RegularExpressions.Regex.IsMatch(parsed.AbsolutePath, "^/[^/]+/image/upload/.+", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
            throw new ServiceException("Ảnh phải được tải lên Cloudinary (URL HTTPS hợp lệ, tối đa 1000 ký tự).", 400);
        return url;
    }
}
