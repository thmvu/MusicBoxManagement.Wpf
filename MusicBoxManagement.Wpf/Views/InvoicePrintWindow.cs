using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;
using MusicBoxManagement.Wpf.Printing;
using MusicBoxManagement.Wpf.ViewModels;

namespace MusicBoxManagement.Wpf.Views
{
    public sealed class InvoicePrintWindow : Window
    {
        private readonly InvoicePrintViewModel viewModel;
        private readonly DocumentPageView page = new DocumentPageView();
        private readonly TextBlock pageLabel = new TextBlock { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 12, 0) };
        private readonly Button previous = new Button { Content = "Trang trước" }, next = new Button { Content = "Trang sau" };
        private int pageNumber;
        public Button PrintButton { get; } = new Button { Content = "Chọn máy in / In", Margin = new Thickness(0, 0, 12, 0) };
        public InvoicePrintWindow(InvoicePrintViewModel viewModel, IInvoicePrinter printer = null)
        {
            this.viewModel = viewModel; DataContext = viewModel;
            Title = "Music Box — Xem trước / In hóa đơn"; Width = 960; Height = 820; MinWidth = 760; MinHeight = 600;
            WindowStartupLocation = WindowStartupLocation.CenterOwner; FontFamily = new FontFamily("Segoe UI"); FontSize = 14;
            Background = new SolidColorBrush(Color.FromRgb(242, 244, 245));
            var root = new Grid { Margin = new Thickness(20) }; Content = root;
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition());
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var header = new DockPanel { Margin = new Thickness(0, 0, 0, 14) }; root.Children.Add(header);
            var close = new Button { Content = "Đóng" }; DockPanel.SetDock(close, Dock.Right); header.Children.Add(close);
            DockPanel.SetDock(PrintButton, Dock.Right); header.Children.Add(PrintButton);
            header.Children.Add(new TextBlock { Text = "Phiếu hóa đơn", FontSize = 24, FontWeight = FontWeights.SemiBold });
            PrintButton.SetBinding(IsEnabledProperty, new Binding(nameof(viewModel.CanPrint)));
            PrintButton.Click += async (sender, args) => await viewModel.PrintAsync(printer ?? new InvoicePrinter());
            close.Click += (sender, args) => { if (!viewModel.IsBusy) Close(); };
            Closing += (sender, args) => { if (viewModel.IsBusy) args.Cancel = true; };
            var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, Content = page };
            Grid.SetRow(scroll, 1); root.Children.Add(scroll);
            var navigation = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 12, 0, 12) };
            navigation.Children.Add(previous); navigation.Children.Add(pageLabel); navigation.Children.Add(next);
            Grid.SetRow(navigation, 2); root.Children.Add(navigation);
            previous.Click += (sender, args) => { if (pageNumber > 0) { pageNumber--; UpdatePage(); } };
            next.Click += (sender, args) => { if (viewModel.Paginator != null && pageNumber + 1 < viewModel.Paginator.PageCount) { pageNumber++; UpdatePage(); } };
            var status = new TextBlock { TextWrapping = TextWrapping.Wrap }; status.SetBinding(TextBlock.TextProperty, new Binding(nameof(viewModel.Status)));
            Grid.SetRow(status, 3); root.Children.Add(status);
            viewModel.PropertyChanged += (sender, args) => { if (args.PropertyName == nameof(viewModel.Document)) { pageNumber = 0; UpdatePage(); } };
            UpdatePage();
        }
        private void UpdatePage()
        {
            var paginator = viewModel.Paginator; page.DocumentPaginator = paginator;
            page.Visibility = paginator == null ? Visibility.Collapsed : Visibility.Visible;
            if (paginator != null) { page.Width = paginator.PageSize.Width; page.Height = paginator.PageSize.Height; page.PageNumber = pageNumber; }
            previous.IsEnabled = paginator != null && pageNumber > 0;
            next.IsEnabled = paginator != null && pageNumber + 1 < paginator.PageCount;
            pageLabel.Text = paginator == null ? "" : "Trang " + (pageNumber + 1) + " / " + paginator.PageCount;
        }
    }
}
