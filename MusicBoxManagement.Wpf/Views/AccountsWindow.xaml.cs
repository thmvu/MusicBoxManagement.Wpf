using System.Windows;
using System.Windows.Controls;
using MusicBoxManagement.Wpf.Models;
using MusicBoxManagement.Wpf.ViewModels;

namespace MusicBoxManagement.Wpf.Views
{
    public partial class AccountsWindow : Window
    {
        private readonly AccountsViewModel viewModel;
        private string formIdentity;
        public AccountsWindow(AccountsViewModel viewModel)
        {
            InitializeComponent(); this.viewModel = viewModel; DataContext = viewModel;
            formIdentity = viewModel.FormIdentity;
            Loaded += async (s, e) => await viewModel.ReloadAsync();
            Closing += (s, e) => { if (viewModel.IsBusy) e.Cancel = true; };
            viewModel.PropertyChanged += (s, e) => { if (e.PropertyName == nameof(viewModel.FormIdentity) && formIdentity != viewModel.FormIdentity) { formIdentity = viewModel.FormIdentity; ClearPasswords(); } };
        }
        private void ClearPasswords() { PasswordInput.Clear(); RepeatInput.Clear(); }
        private async void Reload_Click(object sender, RoutedEventArgs args) { ClearPasswords(); await viewModel.ReloadAsync(); }
        private void New_Click(object sender, RoutedEventArgs args) { ClearPasswords(); viewModel.StartNew(); }
        private void Reset_Click(object sender, RoutedEventArgs args) { ClearPasswords(); viewModel.StartReset(); }
        private void Selection_Changed(object sender, SelectionChangedEventArgs args)
        { if (viewModel == null || !viewModel.CanSelect) return; ClearPasswords(); viewModel.Select(AccountsTable.SelectedItem as UserAccount); }
        private async void Prepare_Click(object sender, RoutedEventArgs args) => await viewModel.PrepareAsync();
        private void Keep_Click(object sender, RoutedEventArgs args) { ClearPasswords(); viewModel.Keep(); }
        private async void Confirm_Click(object sender, RoutedEventArgs args)
        {
            if (!viewModel.CanConfirm) return;
            var password = PasswordInput.Password; var repeat = RepeatInput.Password; ClearPasswords();
            if (viewModel.NeedsPassword && password != repeat) { viewModel.PasswordError(); return; }
            await viewModel.ConfirmAsync(password);
        }
        private void Close_Click(object sender, RoutedEventArgs args) { if (!viewModel.IsBusy) { ClearPasswords(); Close(); } }
    }
}
