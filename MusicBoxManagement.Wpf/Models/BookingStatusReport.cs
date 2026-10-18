using System;
using System.Collections.Generic;

namespace MusicBoxManagement.Wpf.Models
{
    public sealed class BookingStatusReport
    {
        public DateTime FromDate { get; internal set; }
        public DateTime ToDate { get; internal set; }
        public DateTimeOffset CheckedAt { get; internal set; }
        public int TotalCount { get; internal set; }
        public IReadOnlyList<BookingStatusRow> Rows { get; internal set; }
    }

    public sealed class BookingStatusRow
    {
        public string Status { get; internal set; }
        public string Label { get; internal set; }
        public int Count { get; internal set; }
    }
}
