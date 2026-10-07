using System.Windows;
using MusicBoxManagement.Wpf.Models;
using MusicBoxManagement.Wpf.Services;
using MusicBoxManagement.Wpf.ViewModels;

namespace MusicBoxManagement.Wpf.Views
{
    public partial class ReservationsWindow : Window
    {
        private readonly ReservationsViewModel viewModel;
        private bool openingBooking;
        public ReservationsWindow(StaffReservationService service, LoginSession session)
            : this(new ReservationsViewModel(service, session)) { }
        public ReservationsWindow(ReservationsViewModel viewModel)
        {
            InitializeComponent(); this.viewModel = viewModel; DataContext = viewModel;
            Loaded += async (sender, args) => await viewModel.SearchAsync();
            Closing += (sender, args) => { if (viewModel.IsBusy) args.Cancel = true; };
        }
        private async void Search_Click(object sender, RoutedEventArgs args) => await viewModel.SearchAsync();
        private void Cancel_Click(object sender, RoutedEventArgs args) => viewModel.RequestCancel();
        private void Keep_Click(object sender, RoutedEventArgs args) => viewModel.KeepBooking();
        private async void Confirm_Click(object sender, RoutedEventArgs args) => await viewModel.ConfirmCancelAsync();
        private void CheckIn_Click(object sender, RoutedEventArgs args) => viewModel.RequestCheckIn();
        private async void ConfirmCheckIn_Click(object sender, RoutedEventArgs args) => await viewModel.ConfirmCheckInAsync();
        private async void Create_Click(object sender, RoutedEventArgs args)
        {
            if (openingBooking) return; openingBooking = true;
            try
            {
                var draft = await viewModel.PrepareBookingAsync(); if (draft == null) return;
                new GuestBookingWindow(draft.Service, draft) { Owner = this }.ShowDialog();
                await viewModel.AfterBookingAsync(draft.LastCreatedReservationId);
            }
            finally { openingBooking = false; }
        }
    }
}
