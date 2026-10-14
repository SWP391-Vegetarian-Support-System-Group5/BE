namespace BLL.Common;

public static class MediaUrlHelpers
{
    public static string? BuildCloudinaryVideoThumbnailUrl(string? videoUrl)
    {
        if (string.IsNullOrWhiteSpace(videoUrl) ||
            !Uri.TryCreate(videoUrl, UriKind.Absolute, out var uri) ||
            !uri.Host.EndsWith("cloudinary.com", StringComparison.OrdinalIgnoreCase))
            return null;

        const string marker = "/video/upload/";
        var markerIndex = uri.AbsolutePath.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (markerIndex < 0) return null;

        var prefix = uri.AbsolutePath[..(markerIndex + marker.Length)];
        var assetPath = uri.AbsolutePath[(markerIndex + marker.Length)..];
        var extensionIndex = assetPath.LastIndexOf('.');
        var imagePath = extensionIndex >= 0 ? $"{assetPath[..extensionIndex]}.jpg" : $"{assetPath}.jpg";

        return new UriBuilder(uri)
        {
            Path = $"{prefix}c_fill,w_900,h_506,q_auto,so_auto/{imagePath}",
            Query = string.Empty
        }.Uri.ToString();
    }
}
