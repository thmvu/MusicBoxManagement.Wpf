using System;
using System.Collections.Generic;
using System.Globalization;

namespace MusicBoxManagement.Wpf.Models
{
    public sealed class RoomUsageChoice
    {
        public int RoomId { get; internal set; }
        public string Label { get; internal set; }
    }

    public sealed class RoomUsageReport
    {
        public DateTime FromDate { get; internal set; }
        public DateTime ToDate { get; internal set; }
        public int? RoomId { get; internal set; }
        public DateTimeOffset CheckedAt { get; internal set; }
        public bool CanExport { get; internal set; }
        public IReadOnlyList<RoomUsageChoice> RoomChoices { get; internal set; }
        public IReadOnlyList<RoomUsageRow> Rooms { get; internal set; }
        public int CompletedCount { get; internal set; }
        public decimal UsedMinutes { get; internal set; }
        public decimal AvailableMinutes { get; internal set; }
        public decimal? UtilizationPercent => AvailableMinutes == 0 ? (decimal?)null : UsedMinutes / AvailableMinutes * 100m;
        public string UsedMinutesLabel => RoomUsageRow.MinutesLabel(UsedMinutes);
        public string AvailableMinutesLabel => RoomUsageRow.MinutesLabel(AvailableMinutes);
        public string UtilizationLabel => RoomUsageRow.PercentLabel(UtilizationPercent);
    }

    public sealed class RoomUsageRow
    {
        public int RoomId { get; internal set; }
        public string RoomCode { get; internal set; }
        public string RoomName { get; internal set; }
        public bool IsActive { get; internal set; }
        public DateTimeOffset CreatedAt { get; internal set; }
        public int CompletedCount { get; internal set; }
        public decimal UsedMinutes { get; internal set; }
        public decimal AvailableMinutes { get; internal set; }
        public decimal? UtilizationPercent => AvailableMinutes == 0 ? (decimal?)null : UsedMinutes / AvailableMinutes * 100m;
        public string ActiveLabel => IsActive ? "Đang mở" : "Đã khóa";
        public string UsedMinutesLabel => MinutesLabel(UsedMinutes);
        public string AvailableMinutesLabel => MinutesLabel(AvailableMinutes);
        public string UtilizationLabel => PercentLabel(UtilizationPercent);
        internal static string MinutesLabel(decimal value) => value.ToString("N2", CultureInfo.GetCultureInfo("vi-VN"));
        internal static string PercentLabel(decimal? value) => value.HasValue ? value.Value.ToString("N2", CultureInfo.GetCultureInfo("vi-VN")) + " %" : "Không có dữ liệu";
    }
}
