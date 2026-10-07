using System;

namespace MusicBoxManagement.Wpf.Models
{
    public sealed class SessionExtensionCheck
    {
        public bool CanExtend { get; internal set; }
        public string Reason { get; internal set; }
        public DateTimeOffset ExpectedEndTime { get; internal set; }
        public DateTimeOffset NewEndTime { get; internal set; }
        public DateTimeOffset MaximumEndTime { get; internal set; }
        public DateTimeOffset CheckedAt { get; internal set; }
    }

    public sealed class SessionExtensionException : InvalidOperationException
    {
        public DateTimeOffset MaximumEndTime { get; }
        internal SessionExtensionException(string reason, DateTimeOffset maximumEndTime) : base(reason)
        { MaximumEndTime = maximumEndTime; }
    }
}
