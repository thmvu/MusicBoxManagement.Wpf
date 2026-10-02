using System;
using System.Threading.Tasks;
using System.Windows;
using MusicBoxManagement.Wpf.Models;
using MusicBoxManagement.Wpf.Services;
using MusicBoxManagement.Wpf.ViewModels;

namespace MusicBoxManagement.Wpf.Views
{
    public partial class MainWindow : Window
    {
        private readonly MainViewModel viewModel;
        private readonly AuthenticationService authentication;
        private readonly PermissionService permissions;
        private LoginSession session;

        public MainWindow(MainViewModel viewModel, AuthenticationService authentication, PermissionService permissions)
        {
            InitializeComponent();
            this.viewModel = viewModel;
            this.authentication = authentication;
            this.permissions = permissions;
            DataContext = viewModel;
        }

        private async void Window_Loaded(object sender, RoutedEventArgs e)
        {
            await viewModel.RefreshAsync();
            await PrepareLoginAsync();
        }

        private async void Refresh_Click(object sender, RoutedEventArgs e)
        {
            await viewModel.RefreshAsync();
            await PrepareLoginAsync();
        }

        private async Task PrepareLoginAsync()
        {
            LoginButton.IsEnabled = false;
            try
            {
                var needsSetup = await Task.Run(() => authentication.NeedsSetup());
                LoginButton.Content = needsSetup ? "Thiết lập Admin" : "Đăng nhập nhân viên";
                LoginButton.IsEnabled = true;
            }
            catch (Exception) { ModeText.Text = "Chưa đọc được tài khoản. Hãy thử làm mới."; }
        }

        private async void Login_Click(object sender, RoutedEventArgs e)
        {
            LoginButton.IsEnabled = false;
            try
            {
                var needsSetup = await Task.Run(() => authentication.NeedsSetup());
                var dialog = new AuthenticationWindow(authentication, needsSetup) { Owner = this };
                if (dialog.ShowDialog() == true)
                {
                    session = dialog.Session;
                    await RefreshAccessAsync();
                }
            }
            catch (Exception) { MessageBox.Show(this, "Không đọc được tài khoản. Hãy thử làm mới.", "Music Box"); }
            finally { await PrepareLoginAsync(); }
        }

        private async void RefreshAccess_Click(object sender, RoutedEventArgs e) => await RefreshAccessAsync();

        private async Task RefreshAccessAsync()
        {
            RefreshAccessButton.IsEnabled = false;
            try
            {
                var currentSession = session;
                var access = await Task.Run(() => permissions.GetStaffAccess(currentSession));
                if (session != currentSession) return;
                StaffIdentity.Text = access.FullName + " — " + access.Role;
                PermissionsTable.ItemsSource = access.Permissions;
                ModeText.Text = "Nhân viên: " + access.Role;
                CurrentAreaText.Text = "Khu vực nhân viên";
                StaffPanel.Visibility = Visibility.Visible;
                GuestPanel.Visibility = Visibility.Collapsed;
                LoginButton.Visibility = Visibility.Collapsed;
                LogoutButton.Visibility = Visibility.Visible;
                AccessStatus.Text = "Đã kiểm tra " + access.Permissions.Count + " quyền hiện hành. Các màn hình nghiệp vụ sẽ làm ở bước tiếp theo.";
            }
            catch (UnauthorizedAccessException error)
            {
                ReturnToGuest();
                MessageBox.Show(this, error.Message, "Music Box");
            }
            catch (Exception)
            {
                ReturnToGuest();
                MessageBox.Show(this, "Không kiểm tra được quyền. Hãy đăng nhập lại khi dữ liệu sẵn sàng.", "Music Box");
            }
            finally { RefreshAccessButton.IsEnabled = true; }
        }

        private async void Logout_Click(object sender, RoutedEventArgs e)
        {
            ReturnToGuest();
            await viewModel.RefreshAsync();
            await PrepareLoginAsync();
        }

        private void ReturnToGuest()
        {
            authentication.Logout(session);
            session = null;
            PermissionsTable.ItemsSource = null;
            StaffIdentity.Text = "";
            ModeText.Text = "Chế độ Khách";
            CurrentAreaText.Text = "Loại phòng";
            StaffPanel.Visibility = Visibility.Collapsed;
            GuestPanel.Visibility = Visibility.Visible;
            LoginButton.Visibility = Visibility.Visible;
            LogoutButton.Visibility = Visibility.Collapsed;
        }
    }
}
