namespace BLL.Common;

public class ServiceException(string message, int statusCode = 400, IEnumerable<string>? errors = null) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
    public IReadOnlyCollection<string> Errors { get; } = errors?.ToArray() ?? Array.Empty<string>();
}
