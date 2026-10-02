using System;
using System.Data.SQLite;
using System.Threading.Tasks;
using System.Windows;
using MusicBoxManagement.Wpf.Models;
using MusicBoxManagement.Wpf.Services;

namespace MusicBoxManagement.Wpf.Views
{
    public partial class AuthenticationWindow : Window
    {
        private readonly AuthenticationService service;
        private readonly bool setup;
        private bool isBusy;
        public LoginSession Session { get; private set; }

        public AuthenticationWindow(AuthenticationService service, bool setup)
        {
            InitializeComponent();
            this.service = service;
            this.setup = setup;
            Heading.Text = setup ? "Thiết lập Admin đầu tiên" : "Đăng nhập nhân viên";
            Description.Text = setup ? "Tạo tài khoản quản trị cho máy này. Khách vẫn sử dụng mà không cần đăng nhập."
                : "Dùng tài khoản nội bộ để truy cập khu vực nhân viên.";
            FullNameFields.Visibility = ConfirmationFields.Visibility = setup ? Visibility.Visible : Visibility.Collapsed;
            SubmitButton.Content = setup ? "Tạo Admin" : "Đăng nhập";
            Loaded += (sender, args) => UserNameBox.Focus();
            Closing += (sender, args) => { if (isBusy) args.Cancel = true; };
        }

        private async void Submit_Click(object sender, RoutedEventArgs e)
        {
            if (isBusy) return;
            var userName = UserNameBox.Text;
            var fullName = FullNameBox.Text;
            var password = PasswordInput.Password;
            if (setup && password != ConfirmInput.Password)
            { StatusText.Text = "Hai lần nhập mật khẩu chưa khớp."; return; }
            isBusy = true;
            Fields.IsEnabled = SubmitButton.IsEnabled = CancelButton.IsEnabled = false;
            StatusText.Text = setup ? "Đang tạo tài khoản…" : "Đang kiểm tra đăng nhập…";
            try
            {
                Session = await Task.Run(() => setup
                    ? service.SetupAdminAsync(userName, fullName, password)
                    : service.LoginAsync(userName, password));
                isBusy = false;
                DialogResult = true;
            }
            catch (ArgumentException error) { StatusText.Text = error.Message; }
            catch (UnauthorizedAccessException error) { StatusText.Text = error.Message; }
            catch (InvalidOperationException error) { StatusText.Text = error.Message; }
            catch (SQLiteException error)
            {
                StatusText.Text = error.ResultCode == SQLiteErrorCode.Busy || error.ResultCode == SQLiteErrorCode.Locked
                    ? "Dữ liệu đang bận. Hãy thử lại sau." : "Không lưu được dữ liệu tài khoản. Hãy thử lại.";
            }
            catch (Exception)
            { StatusText.Text = "Không truy cập được dữ liệu tài khoản. Kiểm tra thư mục dữ liệu rồi thử lại."; }
            finally
            {
                password = null;
                PasswordInput.Clear();
                ConfirmInput.Clear();
                isBusy = false;
                Fields.IsEnabled = SubmitButton.IsEnabled = CancelButton.IsEnabled = true;
            }
        }
    }
}
