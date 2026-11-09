using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BLL.Common
{
    public sealed class CloudinarySettings
    {
        public string CloudName { get; init; } = string.Empty;
        public string ApiKey { get; init; } = string.Empty;
        public string ApiSecret { get; init; } = string.Empty;
        public string VideoFolder { get; init; } = "veggiemate/videos";
        public string ImageFolder { get; init; } = "veggiemate/images";
        public int UploadRequestTimeoutSeconds { get; init; } = 300;
        public int UploadTotalTimeoutSeconds { get; init; } = 900;

        public bool IsConfigured =>
            !string.IsNullOrWhiteSpace(CloudName)
            && !string.IsNullOrWhiteSpace(ApiKey)
            && !string.IsNullOrWhiteSpace(ApiSecret);
    }
}
