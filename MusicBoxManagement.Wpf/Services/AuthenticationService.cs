using System;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.AspNet.Identity;
using MusicBoxManagement.Wpf.Data;
using MusicBoxManagement.Wpf.Models;

namespace MusicBoxManagement.Wpf.Services
{
    public sealed class AuthenticationService
    {
        private readonly SqliteDatabase database;
        public AuthenticationService(SqliteDatabase database) { this.database = database; }

        public void Logout(LoginSession session)
        {
            if (session != null) session.IsSignedOut = true;
        }

        public bool NeedsSetup()
        {
            database.Initialize();
            using (var connection = database.OpenConnection())
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT COUNT(*) FROM AspNetUsers;";
                return Convert.ToInt64(command.ExecuteScalar()) == 0;
            }
        }

        private static UserManager<ApplicationUser> Manager(SqliteIdentityStore store)
        {
            var manager = new UserManager<ApplicationUser>(store)
            {
                PasswordHasher = new Pbkdf2PasswordHasher(),
                PasswordValidator = new PasswordValidator { RequiredLength = 8 }
            };
            manager.UserValidator = new UserValidator<ApplicationUser>(manager) { AllowOnlyAlphanumericUserNames = false };
            return manager;
        }

        public async Task<LoginSession> SetupAdminAsync(string userName, string fullName, string password)
        {
            userName = (userName ?? "").Trim();
            fullName = (fullName ?? "").Trim();
            if (!Regex.IsMatch(userName, @"\A[A-Za-z0-9._-]{3,100}\z"))
                throw new ArgumentException("Tên đăng nhập từ 3–100 ký tự, dùng chữ không dấu, số, dấu chấm, gạch dưới hoặc gạch ngang.");
            if (fullName.Length < 1 || fullName.Length > 100)
                throw new ArgumentException("Họ tên từ 1–100 ký tự.");
            if (password == null || password.Length < 8 || password.Length > 128)
                throw new ArgumentException("Mật khẩu từ 8–128 ký tự.");
            database.Initialize();
            using (var connection = database.OpenConnection())
            using (var store = new SqliteIdentityStore(connection))
            using (var manager = Manager(store))
            {
                var user = new ApplicationUser { UserName = userName, FullName = fullName };
                // UserManager validates and hashes outside the write transaction.
                var result = await manager.CreateAsync(user, password);
                if (!result.Succeeded) throw new ArgumentException("Không tạo được tài khoản. Kiểm tra tên đăng nhập và mật khẩu.");
                result = await manager.AddToRoleAsync(user.Id, "Admin");
                if (!result.Succeeded) throw new InvalidOperationException("Không gán được role Admin.");
                using (var transaction = SqliteDatabase.BeginWriteTransaction(connection))
                {
                    store.Transaction = transaction;
                    using (var command = connection.CreateCommand())
                    {
                        command.Transaction = transaction;
                        command.CommandText = "SELECT COUNT(*) FROM AspNetUsers;";
                        if (Convert.ToInt64(command.ExecuteScalar()) != 0)
                            throw new InvalidOperationException("Admin đầu tiên đã được thiết lập. Hãy đóng cửa sổ và đăng nhập.");
                    }
                    store.SaveChanges();
                    AuditService.WriteStaff(connection, transaction, user.Id, "User.Bootstrap",
                        "ApplicationUser", user.Id, "Thiết lập Admin đầu tiên.");
                    transaction.Commit();
                }
                return new LoginSession(user.Id, user.SecurityStamp);
            }
        }

        public async Task<LoginSession> LoginAsync(string userName, string password)
        {
            if (string.IsNullOrWhiteSpace(userName) || userName.Length > 100 || password == null || password.Length > 128)
                throw new UnauthorizedAccessException("Tên đăng nhập hoặc mật khẩu không đúng.");
            database.Initialize();
            using (var connection = database.OpenConnection())
            using (var store = new SqliteIdentityStore(connection))
            using (var manager = Manager(store))
            {
                var user = await manager.FindByNameAsync(userName.Trim());
                if (user == null || !user.IsActive || !await manager.CheckPasswordAsync(user, password))
                    throw new UnauthorizedAccessException("Tên đăng nhập hoặc mật khẩu không đúng, hoặc tài khoản đã khóa.");
                var session = new LoginSession(user.Id, user.SecurityStamp);
                // Re-read after password verification; account changes during the
                // costly hash must invalidate this attempt as well.
                using (var transaction = SqliteDatabase.BeginWriteTransaction(connection))
                {
                    new PermissionService(database).ReadAccess(session, connection, transaction);
                    AuditService.WriteStaff(connection, transaction, user.Id, "User.Login",
                        "ApplicationUser", user.Id, "Đăng nhập khu vực nhân viên.");
                    transaction.Commit();
                }
                return session;
            }
        }

    }
}
