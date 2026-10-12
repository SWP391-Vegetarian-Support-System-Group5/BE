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
    }
}
