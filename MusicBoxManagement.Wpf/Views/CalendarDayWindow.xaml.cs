using System;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using MusicBoxManagement.Wpf.Models;
using MusicBoxManagement.Wpf.Services;
using MusicBoxManagement.Wpf.ViewModels;

namespace MusicBoxManagement.Wpf.Views
{
    public partial class CalendarDayWindow : Window
    {
        private readonly CalendarDayViewModel viewModel;
        private const double Header = 0, MinutesScale = 1.5, Gutter = 0;
        private double columnWidth = 220;
        private bool navigating;
        public CalendarDayWindow(ICalendarService service, LoginSession session) : this(new CalendarDayViewModel(service, session)) { }
        public CalendarDayWindow(CalendarDayViewModel viewModel)
        {
            InitializeComponent(); this.viewModel = viewModel; DataContext = viewModel;
            Width = Math.Min(Width, SystemParameters.WorkArea.Width); Height = Math.Min(Height, SystemParameters.WorkArea.Height);
            viewModel.Rooms.CollectionChanged += Rooms_Changed;
            Closed += (s, e) => viewModel.Rooms.CollectionChanged -= Rooms_Changed;
        }
        private void Rooms_Changed(object sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e) => Draw();
        private async void Loaded_Window(object sender, RoutedEventArgs e) => await viewModel.LoadAsync();
        private async void Load_Click(object sender, RoutedEventArgs e) => await UserFeedback.RunAsync(this, () => viewModel.LoadAsync(), () => viewModel.Status);
        private async void Previous_Click(object sender, RoutedEventArgs e) => await Navigate(() => viewModel.MoveAsync(-1));
        private async void Next_Click(object sender, RoutedEventArgs e) => await Navigate(() => viewModel.MoveAsync(1));
        private async void Today_Click(object sender, RoutedEventArgs e) => await Navigate(() => viewModel.TodayAsync());
        private async void Day_Click(object sender, RoutedEventArgs e) => await Navigate(() => viewModel.ChangeModeAsync(false));
        private async void Week_Click(object sender, RoutedEventArgs e) => await Navigate(() => viewModel.ChangeModeAsync(true));
        private async System.Threading.Tasks.Task Navigate(Func<System.Threading.Tasks.Task> action)
        { navigating = true; try { await action(); } finally { navigating = false; } }
        private async void Month_Changed(object sender, SelectionChangedEventArgs e)
        { if (IsLoaded && viewModel != null && !viewModel.IsBusy && !navigating) await viewModel.LoadAsync(); }
        private async void Room_Changed(object sender, SelectionChangedEventArgs e)
        { if (IsLoaded && viewModel != null && !viewModel.IsBusy && !navigating) await viewModel.LoadAsync(); }
        private void Timeline_Scrolled(object sender, ScrollChangedEventArgs e)
        { if (HeaderOffset != null) HeaderOffset.X = -e.HorizontalOffset; if (RulerOffset != null) RulerOffset.Y = -e.VerticalOffset; }
        private void Timeline_Resized(object sender, SizeChangedEventArgs e) { if (viewModel != null) Draw(); }
        private void Closing_Window(object sender, CancelEventArgs e) { if (viewModel.IsBusy) e.Cancel = true; }
        private static Brush Color(string value) => (Brush)new BrushConverter().ConvertFromString(value);
        private void Put(UIElement element, double x, double y) { Canvas.SetLeft(element, x); Canvas.SetTop(element, y); Timeline.Children.Add(element); }
        private static string StatusLabel(string value) => value == "Inactive" ? "Đang khóa" : value == "Occupied" ? "Đang có khách" : value == "Reserved" ? "Tới lượt đặt" : "Đang trống";
        private void Draw()
        {
            Timeline.Children.Clear(); TimelineHeaders.Children.Clear(); TimeRuler.Children.Clear();
            var columns = viewModel.IsWeek ? 7 : Math.Max(1, viewModel.Rooms.Count);
            columnWidth = Math.Max(viewModel.IsWeek ? 190 : 165, (Math.Max(400, TimelineScroll.ViewportWidth) - 2) / columns);
            Timeline.Width = columnWidth * columns; Timeline.Height = 840 * MinutesScale + 16;
            TimelineHeaders.Width = Timeline.Width; TimeRuler.Height = Timeline.Height;
            if (viewModel.Rooms.Count == 0) { Put(new TextBlock { Text = "Chưa có lịch để hiển thị. Chọn ngày/phòng và tải lịch.", Margin = new Thickness(16) }, 0, 0); return; }
            Put(new Rectangle { Width = Timeline.Width, Height = 60 * MinutesScale, Fill = Color("#F3F0E7") }, 0, Header + 180 * MinutesScale);
            for (var minutes = 0; minutes <= 840; minutes += 30)
            {
                var y = Header + minutes * MinutesScale;
                Put(new Line { X1 = Gutter, X2 = Timeline.Width, Y1 = 0, Y2 = 0, Stroke = Color(minutes % 60 == 0 ? "#D9DED4" : "#ECEFE9"), StrokeThickness = 1 }, 0, y);
                if (minutes % 60 == 0) { var label = new TextBlock { Text = TimeSpan.FromMinutes(540 + minutes).ToString(@"hh\:mm"), FontSize = 12, FontWeight = FontWeights.SemiBold, Foreground = Color("#667168") }; Canvas.SetLeft(label, 10); Canvas.SetTop(label, Math.Max(0, y - 8)); TimeRuler.Children.Add(label); }
            }
            if (viewModel.IsWeek)
            {
                var start = viewModel.Rooms[0].Range.Start;
                var labels = new[] { "Thứ Hai", "Thứ Ba", "Thứ Tư", "Thứ Năm", "Thứ Sáu", "Thứ Bảy", "Chủ nhật" };
                var events = viewModel.Rooms.SelectMany(room => room.Events.Select(item => Tuple.Create(room, item))).ToList();
                for (var day = 0; day < 7; day++) DrawColumn(day, start.AddDays(day), labels[day] + "\n" + start.AddDays(day).ToOffset(BookingHours.VietnamOffset).ToString("dd/MM/yyyy"), events);
            }
            else
            {
                var column = 0;
                foreach (var room in viewModel.Rooms) DrawColumn(column++, room.Range.Start,
                    room.RoomCode + " — " + room.RoomName + "\n" + StatusLabel(room.CurrentStatus) + " (hiện tại)", room.Events.Select(item => Tuple.Create(room, item)));
            }
        }
        private void DrawColumn(int column, DateTimeOffset day, string heading, System.Collections.Generic.IEnumerable<Tuple<RoomSchedule, RoomScheduleEvent>> events)
        {
                var x = Gutter + column * columnWidth;
                var header = new Border { Width = columnWidth, Height = 68, Background = Color("#EBEEE7"), BorderBrush = Color("#D9DED4"), BorderThickness = new Thickness(0,0,1,1),
                    Child = new TextBlock { Text = heading, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(12,10,10,4), FontSize = 13, FontWeight = FontWeights.SemiBold } };
                Canvas.SetLeft(header, x); TimelineHeaders.Children.Add(header);
                Put(new Line { X1 = 0, X2 = 0, Y1 = Header, Y2 = Timeline.Height, Stroke = Color("#D9DED4") }, x, 0);
                var from = day.AddHours(9); var to = day.AddHours(23);
                var visible = events.Where(pair => pair.Item2.Start < to && DisplayEnd(pair.Item2) > from).OrderBy(pair => pair.Item2.Start).ToList();
                // Greedy lanes preserve overlapping actual overdue usage and later bookings.
                var lanes = new System.Collections.Generic.List<DateTimeOffset>();
                var assigned = new System.Collections.Generic.List<int>();
                foreach (var pair in visible) { var item = pair.Item2; var lane = lanes.FindIndex(end => end <= item.Start); if (lane < 0) { lane = lanes.Count; lanes.Add(DisplayEnd(item)); } else lanes[lane] = DisplayEnd(item); assigned.Add(lane); }
                for (var i = 0; i < visible.Count; i++)
                {
                    var room = visible[i].Item1; var item = visible[i].Item2; var start = item.Start < from ? from : item.Start; var end = DisplayEnd(item) > to ? to : DisplayEnd(item);
                    var width = (columnWidth - 12) / Math.Max(1, lanes.Count); var height = Math.Max(8, (end - start).TotalMinutes * MinutesScale);
                    var panel = new Grid { ClipToBounds = true };
                    var fill = item.Kind == "Confirmed" ? "#E2EBF1" : item.Kind == "Completed" ? "#ECEDEA" : "#E0EBDD";
                    panel.Children.Add(new Rectangle { Fill = Color(fill), RadiusX = 5, RadiusY = 5 });
                    var accent = item.IsOverdue ? "#955D35" : item.Kind == "Confirmed" ? "#516C86" : item.Kind == "Completed" ? "#737B77" : "#51634E";
                    panel.Children.Add(new Border { Background = Color(accent), Height = Math.Min(5, height), VerticalAlignment = VerticalAlignment.Top, CornerRadius = new CornerRadius(3,3,0,0) });
                    if (item.Kind == "Active" && item.ExpectedEnd.HasValue && item.ExpectedEnd.Value > item.End)
                    {
                        var expectedStart = item.End < start ? start : item.End;
                        panel.Children.Add(new Rectangle { Fill = Color("#F4F7F5"), Stroke = Color("#627D6D"), StrokeDashArray = new DoubleCollection { 4,3 }, VerticalAlignment = VerticalAlignment.Bottom, Height = Math.Max(0, (end - expectedStart).TotalMinutes * MinutesScale) });
                    }
                    var label = room.RoomCode + " · " + (item.Kind == "Confirmed" ? "Đã đặt" : item.Kind == "Completed" ? "Hoàn tất" : item.Kind == "WalkIn" ? "Khách trực tiếp" : "Đang dùng") + (item.IsOverdue ? " · QUÁ GIỜ" : "") + "\n" + item.Start.ToOffset(BookingHours.VietnamOffset).ToString("HH:mm") + " – " + DisplayEnd(item).ToOffset(BookingHours.VietnamOffset).ToString("HH:mm") + "\n" + item.CustomerName;
                    if (item.ReturnBy.HasValue) label += "\nTrả trước " + item.ReturnBy.Value.ToOffset(BookingHours.VietnamOffset).ToString("HH:mm");
                    panel.Children.Add(new TextBlock { Text = label, TextWrapping = TextWrapping.Wrap, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(9,10,9,3), FontSize = 12 });
                    var button = new Button { Style = (Style)FindResource("TimelineEventButton"), MinHeight = 0, MinWidth = 0, Width = width - 3, Height = height, Padding = new Thickness(0), HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Stretch,
                        Background = Brushes.Transparent, BorderBrush = Color(item.IsOverdue ? "#955D35" : "#BCC8BD"), BorderThickness = new Thickness(item.IsOverdue ? 2 : 1), Content = panel,
                        ToolTip = CalendarDayViewModel.Describe(room, item), Tag = item };
                    System.Windows.Automation.AutomationProperties.SetName(button, CalendarDayViewModel.Describe(room, item));
                    button.Click += (s, e) => viewModel.Select(room, item);
                    Put(button, x + 6 + assigned[i] * width, Header + (start - from).TotalMinutes * MinutesScale);
                }
                var checkedAt = viewModel.Rooms[0].CheckedAt;
                if (checkedAt >= from && checkedAt <= to)
                    Put(new Line { X1 = 0, X2 = columnWidth, Y1 = 0, Y2 = 0, Stroke = Color("#9B835D"), StrokeThickness = 1.5, IsHitTestVisible = false }, x, (checkedAt - from).TotalMinutes * MinutesScale);
                Put(new TextBlock { Text = "12:00–13:00 · Giờ nghỉ", Foreground = Color("#72634C"), FontSize = 12 }, x + 10, Header + 180 * MinutesScale + 30);
        }
        private static DateTimeOffset DisplayEnd(RoomScheduleEvent item) => item.Kind == "Active" && item.ExpectedEnd.HasValue && item.ExpectedEnd.Value > item.End ? item.ExpectedEnd.Value : item.End;
    }
}
