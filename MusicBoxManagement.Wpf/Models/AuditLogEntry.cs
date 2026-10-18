using System;
using System.Collections.Generic;
using System.Globalization;
using MusicBoxManagement.Wpf.Services;

namespace MusicBoxManagement.Wpf.Models
{
    public sealed class AuditLogEntry
    {
        public int AuditLogId { get; internal set; }
        public string ActorType { get; internal set; }
        public string UserId { get; internal set; }
        public string Action { get; internal set; }
        public string EntityName { get; internal set; }
        public string EntityId { get; internal set; }
        public string Description { get; internal set; }
        public DateTimeOffset CreatedAt { get; internal set; }
        // These are current account metadata, not historical snapshots.
        public string UserName { get; internal set; }
        public string FullName { get; internal set; }
        public string CreatedLabel => CreatedAt.ToOffset(BookingHours.VietnamOffset).ToString("HH:mm:ss dd/MM/yyyy", CultureInfo.InvariantCulture);
        public string ActorLabel => ActorType == "Staff" ? "Nhân viên" : ActorType == "Guest" ? "Khách" : "Hệ thống";
        public string UserLabel => ActorType != "Staff" ? "-" : !string.IsNullOrEmpty(FullName) && !string.IsNullOrEmpty(UserName)
            ? FullName + " (" + UserName + ")" : FullName ?? UserName ?? UserId ?? "-";
    }

    public sealed class AuditLogPage
    {
        public IReadOnlyList<AuditLogEntry> Items { get; internal set; }
        public bool HasMore { get; internal set; }
        public int? NextBeforeId { get; internal set; }
        public DateTimeOffset CheckedAt { get; internal set; }
    }
}
