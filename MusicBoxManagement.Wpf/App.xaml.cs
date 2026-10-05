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
                new AuthenticationService(database), new PermissionService(database), roomTypes, new RoomService(database), new ServiceCatalogService(database), new CustomerService(database), new GuestBookingService(database), new GuestReservationService(database));
            MainWindow.Show();
            noShowWorker = new NoShowWorker(database);
            _ = noShowWorker.StartAsync();
        }

        protected override void OnExit(ExitEventArgs e)
        { noShowWorker?.Dispose(); base.OnExit(e); }
    }
}
