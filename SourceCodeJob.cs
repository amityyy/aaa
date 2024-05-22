using System;
using Microsoft.CloudMine.SourceCode.Collectors.Core.Model;

namespace Microsoft.CloudMine.SourceCode.Collectors.Scheduler.Service
{
    public record SourceCodeJob
    {
        public required ServiceNotificationMessage Message { get; init; }
        public required DateTimeOffset UpdateTime { get; init; }
    }
}
