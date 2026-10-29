using System;
using System.IO;
using MusicBoxManagement.Wpf.Data;
using MusicBoxManagement.Wpf.ViewModels;
using MusicBoxManagement.Wpf.Views;

namespace MusicBoxManagement.Wpf.Services
{
    // One provider per application lifetime; never fall back silently after an online write fails.
    public sealed class ApplicationServices
    {
        public Views.MainWindow Window { get; }
        public NoShowWorker Worker { get; }
        public ApplicationServices(string provider, string databaseName, string storageRoot)
        {
            if (provider == "MySql")
            {
                var db = new MySqlDatabase(databaseName); db.Initialize();
                var types = new MySqlRoomTypeService(db);
                Window = new Views.MainWindow(new MainViewModel(types), new MySqlAuthenticationService(db), new MySqlPermissionService(db),
                    types, new MySqlRoomService(db, storageRoot), new MySqlServiceCatalogService(db), new MySqlCustomerService(db),
                    new MySqlGuestBookingService(db, storageRoot), new MySqlGuestReservationService(db),
                    new MySqlStaffReservationService(db, new SystemClock(), storageRoot));
                Worker = new NoShowWorker(db);
            }
            else if (provider == "SQLite")
            {
                var db = new SqliteDatabase(databaseName); db.Initialize();
                var types = new RoomTypeService(db);
                Window = new Views.MainWindow(new MainViewModel(types), new AuthenticationService(db), new PermissionService(db),
                    types, new RoomService(db), new ServiceCatalogService(db), new CustomerService(db),
                    new GuestBookingService(db), new GuestReservationService(db), new StaffReservationService(db));
                Worker = new NoShowWorker(db);
            }
            else throw new ArgumentException("Provider chỉ nhận SQLite hoặc MySql.");
        }
        public static ApplicationServices FromConfiguration()
        {
            var config = System.Configuration.ConfigurationManager.AppSettings;
            var provider = Environment.GetEnvironmentVariable("MUSICBOX_PROVIDER") ?? config["DatabaseProvider"] ?? "SQLite";
            var name = provider == "MySql" ? (Environment.GetEnvironmentVariable("MUSICBOX_MYSQL_DATABASE") ?? config["MySqlDatabase"] ?? MySqlDatabase.DefaultDatabaseName)
                : (Environment.GetEnvironmentVariable("MUSICBOX_SQLITE_PATH") ?? SqliteDatabase.DefaultPath);
            var root = Environment.GetEnvironmentVariable("MUSICBOX_STORAGE_ROOT") ?? (provider == "MySql"
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MusicBoxManagement.Wpf.MySql")
                : Path.GetDirectoryName(SqliteDatabase.DefaultPath));
            return new ApplicationServices(provider, name, root);
        }
    }
}
