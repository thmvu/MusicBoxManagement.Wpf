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
        private const double Header = 62, MinutesScale = 1.4, ColumnWidth = 240, Gutter = 68;
        public CalendarDayWindow(CalendarService service, LoginSession session) : this(new CalendarDayViewModel(service, session)) { }
        public CalendarDayWindow(CalendarDayViewModel viewModel)
        {
            InitializeComponent(); this.viewModel = viewModel; DataContext = viewModel;
            viewModel.Rooms.CollectionChanged += Rooms_Changed;
            Closed += (s, e) => viewModel.Rooms.CollectionChanged -= Rooms_Changed;
        }
        private void Rooms_Changed(object sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e) => Draw();
        private async void Loaded_Window(object sender, RoutedEventArgs e) => await viewModel.LoadAsync();
        private async void Load_Click(object sender, RoutedEventArgs e) => await viewModel.LoadAsync();
        private async void Previous_Click(object sender, RoutedEventArgs e) => await viewModel.MoveAsync(-1);
        private async void Next_Click(object sender, RoutedEventArgs e) => await viewModel.MoveAsync(1);
        private async void Today_Click(object sender, RoutedEventArgs e) => await viewModel.TodayAsync();
        private async void Day_Click(object sender, RoutedEventArgs e) => await viewModel.ChangeModeAsync(false);
        private async void Week_Click(object sender, RoutedEventArgs e) => await viewModel.ChangeModeAsync(true);
        private void Closing_Window(object sender, CancelEventArgs e) { if (viewModel.IsBusy) e.Cancel = true; }
        private static Brush Color(string value) => (Brush)new BrushConverter().ConvertFromString(value);
        private void Put(UIElement element, double x, double y) { Canvas.SetLeft(element, x); Canvas.SetTop(element, y); Timeline.Children.Add(element); }
        private static string StatusLabel(string value) => value == "Inactive" ? "Đang khóa" : value == "Occupied" ? "Đang có khách" : value == "Reserved" ? "Tới lượt đặt" : "Đang trống";
        private void Draw()
        {
            Timeline.Children.Clear(); Timeline.Width = Math.Max(600, Gutter + ColumnWidth * (viewModel.IsWeek ? 7 : viewModel.Rooms.Count)); Timeline.Height = Header + 840 * MinutesScale + 24;
            if (viewModel.Rooms.Count == 0) { Put(new TextBlock { Text = "Chưa có lịch để hiển thị. Chọn ngày/phòng và tải lịch.", Margin = new Thickness(16) }, 0, 0); return; }
            Put(new Rectangle { Width = Timeline.Width, Height = 60 * MinutesScale, Fill = Color("#EEEAE2") }, 0, Header + 180 * MinutesScale);
            for (var minutes = 0; minutes <= 840; minutes += 30)
            {
                var y = Header + minutes * MinutesScale;
                Put(new Line { X1 = Gutter, X2 = Timeline.Width, Y1 = 0, Y2 = 0, Stroke = Color(minutes % 60 == 0 ? "#CAD3D8" : "#E5EAED"), StrokeThickness = 1 }, 0, y);
                Put(new TextBlock { Text = TimeSpan.FromMinutes(540 + minutes).ToString(@"hh\:mm"), FontSize = 12, Foreground = Color("#5C6873") }, 12, y - 8);
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
                var x = Gutter + column * ColumnWidth;
                Put(new Border { Width = ColumnWidth, Height = Header, Background = Color("#E9EEF0"), BorderBrush = Color("#CAD3D8"), BorderThickness = new Thickness(0,0,1,1),
                    Child = new TextBlock { Text = heading, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(10,8,10,4), FontWeight = FontWeights.SemiBold } }, x, 0);
                Put(new Line { X1 = 0, X2 = 0, Y1 = Header, Y2 = Timeline.Height, Stroke = Color("#CAD3D8") }, x, 0);
                var from = day.AddHours(9); var to = day.AddHours(23);
                var visible = events.Where(pair => pair.Item2.Start < to && DisplayEnd(pair.Item2) > from).OrderBy(pair => pair.Item2.Start).ToList();
                // Greedy lanes preserve overlapping actual overdue usage and later bookings.
                var lanes = new System.Collections.Generic.List<DateTimeOffset>();
                var assigned = new System.Collections.Generic.List<int>();
                foreach (var pair in visible) { var item = pair.Item2; var lane = lanes.FindIndex(end => end <= item.Start); if (lane < 0) { lane = lanes.Count; lanes.Add(DisplayEnd(item)); } else lanes[lane] = DisplayEnd(item); assigned.Add(lane); }
                for (var i = 0; i < visible.Count; i++)
                {
                    var room = visible[i].Item1; var item = visible[i].Item2; var start = item.Start < from ? from : item.Start; var end = DisplayEnd(item) > to ? to : DisplayEnd(item);
                    var width = (ColumnWidth - 12) / Math.Max(1, lanes.Count); var height = Math.Max(8, (end - start).TotalMinutes * MinutesScale);
                    var panel = new Grid();
                    var fill = item.Kind == "Confirmed" ? "#DCE8E2" : item.Kind == "Completed" ? "#E2E6E9" : "#B2C8BE";
                    panel.Children.Add(new Rectangle { Fill = Color(fill) });
                    if (item.Kind == "Active" && item.ExpectedEnd.HasValue && item.ExpectedEnd.Value > item.End)
                    {
                        var expectedStart = item.End < start ? start : item.End;
                        panel.Children.Add(new Rectangle { Fill = Color("#F4F7F5"), Stroke = Color("#627D6D"), StrokeDashArray = new DoubleCollection { 4,3 }, VerticalAlignment = VerticalAlignment.Bottom, Height = Math.Max(0, (end - expectedStart).TotalMinutes * MinutesScale) });
                    }
                    var label = room.RoomCode + " · " + (item.Kind == "Confirmed" ? "Đã đặt" : item.Kind == "Completed" ? "Hoàn tất" : item.Kind == "WalkIn" ? "Walk-in · chưa chốt" : "Đang dùng") + (item.IsOverdue ? " · QUÁ GIỜ" : "") + "\n" + item.Start.ToOffset(BookingHours.VietnamOffset).ToString("HH:mm") + " – " + DisplayEnd(item).ToOffset(BookingHours.VietnamOffset).ToString("HH:mm") + "\n" + item.CustomerName;
                    if (item.ReturnBy.HasValue) label += "\nTrả trước " + item.ReturnBy.Value.ToOffset(BookingHours.VietnamOffset).ToString("HH:mm");
                    panel.Children.Add(new TextBlock { Text = label, TextWrapping = TextWrapping.Wrap, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(7,4,7,2), FontSize = 12 });
                    var button = new Button { Width = width - 3, Height = height, Padding = new Thickness(0), HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Stretch,
                        BorderBrush = Color(item.IsOverdue ? "#955D35" : "#81988C"), BorderThickness = new Thickness(item.IsOverdue ? 2 : 1), Content = panel,
                        ToolTip = CalendarDayViewModel.Describe(room, item), Tag = item };
                    System.Windows.Automation.AutomationProperties.SetName(button, CalendarDayViewModel.Describe(room, item));
                    button.Click += (s, e) => viewModel.Select(room, item);
                    Put(button, x + 6 + assigned[i] * width, Header + (start - from).TotalMinutes * MinutesScale);
                }
                Put(new TextBlock { Text = "12:00–13:00 · Giờ nghỉ", Foreground = Color("#72634C"), FontSize = 12 }, x + 10, Header + 180 * MinutesScale + 30);
        }
        private static DateTimeOffset DisplayEnd(RoomScheduleEvent item) => item.Kind == "Active" && item.ExpectedEnd.HasValue && item.ExpectedEnd.Value > item.End ? item.ExpectedEnd.Value : item.End;
    }
}
