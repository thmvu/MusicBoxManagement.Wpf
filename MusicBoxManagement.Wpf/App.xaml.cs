using System.Globalization;
using System.Threading;
using System.Windows;
using MusicBoxManagement.Wpf.Data;
using MusicBoxManagement.Wpf.Services;
using MusicBoxManagement.Wpf.ViewModels;
using MusicBoxManagement.Wpf.Views;

namespace MusicBoxManagement.Wpf
{
    public partial class App : Application
    {
        private NoShowWorker noShowWorker;
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            var culture = CultureInfo.GetCultureInfo("vi-VN");
            Thread.CurrentThread.CurrentCulture = culture;
            Thread.CurrentThread.CurrentUICulture = culture;
            var database = new SqliteDatabase(SqliteDatabase.DefaultPath);
            var roomTypes = new RoomTypeService(database);
            MainWindow = new MainWindow(new MainViewModel(roomTypes),
                new AuthenticationService(database), new PermissionService(database), roomTypes, new RoomService(database), new ServiceCatalogService(database), new CustomerService(database), new GuestBookingService(database), new GuestReservationService(database), new StaffReservationService(database));
            MainWindow.Show();
#if DEBUG
            _ = TestAivenAsync();
#endif
            noShowWorker = new NoShowWorker(database);
            _ = noShowWorker.StartAsync();
        }

#if DEBUG
        private async System.Threading.Tasks.Task TestAivenAsync()
        {
            try
            {
                var version = await AivenDatabase.TestConnectionAsync();
                if (MainWindow == null || !MainWindow.IsVisible) return;
                MessageBox.Show(MainWindow,
                    "WPF đã kết nối Aiven thành công!\nMySQL: " + version,
                    "Kiểm tra Aiven", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (System.Exception ex)
            {
                if (MainWindow == null || !MainWindow.IsVisible) return;
                MessageBox.Show(MainWindow,
                    "Kết nối Aiven thất bại:\n" + ex.Message,
                    "Kiểm tra Aiven", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
#endif

        protected override void OnExit(ExitEventArgs e)
        { noShowWorker?.Dispose(); base.OnExit(e); }
    }
}
