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
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            var culture = CultureInfo.GetCultureInfo("vi-VN");
            Thread.CurrentThread.CurrentCulture = culture;
            Thread.CurrentThread.CurrentUICulture = culture;
            var database = new SqliteDatabase(SqliteDatabase.DefaultPath);
            MainWindow = new MainWindow(new MainViewModel(new RoomTypeService(database)),
                new AuthenticationService(database), new PermissionService(database));
            MainWindow.Show();
        }
    }
}
