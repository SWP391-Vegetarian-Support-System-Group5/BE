using System.ComponentModel.DataAnnotations;

namespace BLL.DTOs;

public record ImageResponse(string? ImageUrl, bool IsGenerated, bool PreviousImageDeleted);
public record GenerateMealImageRequest([MaxLength(300)] string? Style = null);
