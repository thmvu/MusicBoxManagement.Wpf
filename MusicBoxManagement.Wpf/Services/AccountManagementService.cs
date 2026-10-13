using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SQLite;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.AspNet.Identity;
using MusicBoxManagement.Wpf.Data;
using MusicBoxManagement.Wpf.Models;

namespace MusicBoxManagement.Wpf.Services
{
    public sealed class AccountManagementService
    {
        private readonly SqliteDatabase database;
        private readonly PermissionService permissions;
        private readonly IClock clock;
        public AccountManagementService(SqliteDatabase database) : this(database, new SystemClock()) { }
        public AccountManagementService(SqliteDatabase database, IClock clock)
        { this.database = database ?? throw new ArgumentNullException(nameof(database)); this.clock = clock ?? throw new ArgumentNullException(nameof(clock)); permissions = new PermissionService(database); }
        public IReadOnlyList<UserAccount> List(LoginSession actor)
        {
            using (var connection = database.OpenConnection())
            using (var transaction = connection.BeginTransaction(IsolationLevel.ReadCommitted))
            using (var store = new SqliteIdentityStore(connection) { Transaction = transaction })
            {
                permissions.Demand(actor, "User.Manage", connection, transaction);
                var ids = new List<string>();
                using (var command = connection.CreateCommand())
                { command.Transaction = transaction; command.CommandText = "SELECT Id FROM AspNetUsers ORDER BY NormalizedUserName;";
                    using (var reader = command.ExecuteReader()) while (reader.Read()) ids.Add(reader.GetString(0)); }
                return ids.Select(id => Project(store.FindByIdAsync(id).GetAwaiter().GetResult())).ToList().AsReadOnly();
            }
        }
        public async Task<UserAccount> CreateAsync(LoginSession actor, AccountEdit input, string password)
        {
            permissions.Demand(actor, "User.Manage"); var edit = Validate(input); ValidatePassword(password);
            using (var connection = database.OpenConnection())
            using (var store = new SqliteIdentityStore(connection))
            using (var manager = AuthenticationService.Manager(store))
            {
                var user = new ApplicationUser { UserName = edit.UserName, FullName = edit.FullName, IsActive = edit.IsActive };
                Success(await manager.CreateAsync(user, password)); Success(await manager.AddToRoleAsync(user.Id, edit.Role));
                using (var transaction = SqliteDatabase.BeginWriteTransaction(connection))
                {
                    store.Transaction = transaction; permissions.Demand(actor, "User.Manage", connection, transaction);
                    DemandUnique(connection, transaction, user.UserName, user.Id);
                    store.SaveManagedChanges(permissions, actor); DemandAdminRemains(connection, transaction);
                    AuditService.WriteStaff(connection, transaction, actor.UserId, "User.Create", "ApplicationUser", user.Id,
                        "Tạo tài khoản " + user.UserName + "; role " + edit.Role + "; active " + edit.IsActive + ".", clock.UtcNow.ToUniversalTime());
                    transaction.Commit(); return Project(user);
                }
            }
        }
        public Task<UserAccount> UpdateAsync(LoginSession actor, UserAccount original, AccountEdit input)
            => ChangeAsync(actor, original, Validate(input), null);
        public Task<UserAccount> ResetPasswordAsync(LoginSession actor, UserAccount original, string password)
        { ValidatePassword(password); return ChangeAsync(actor, original, null, password); }
        private async Task<UserAccount> ChangeAsync(LoginSession actor, UserAccount original, AccountEdit edit, string password)
        {
            permissions.Demand(actor, "User.Manage");
            if (original == null || string.IsNullOrEmpty(original.UserId)) throw new ArgumentException("Cần chọn tài khoản và tải lại trước khi sửa.");
            using (var connection = database.OpenConnection())
            using (var store = new SqliteIdentityStore(connection))
            using (var manager = AuthenticationService.Manager(store))
            {
                var user = await store.FindByIdAsync(original.UserId);
                DemandOriginal(user, original);
                await store.UpdateAsync(user);
                if (edit != null)
                {
                    if (user.Id == actor.UserId && (!edit.IsActive || edit.Role != "Admin"))
                        throw new InvalidOperationException("Admin không được tự khóa hoặc tự hạ vai trò.");
                    user.UserName = edit.UserName; user.FullName = edit.FullName; user.IsActive = edit.IsActive;
                    if (user.Roles.Single().RoleId != edit.Role)
                    { Success(await manager.RemoveFromRoleAsync(user.Id, user.Roles.Single().RoleId)); Success(await manager.AddToRoleAsync(user.Id, edit.Role)); }
                    // Lock/unlock must not let a pre-lock session revive after reopening the account.
                    if (original.IsActive != edit.IsActive) Success(await manager.UpdateSecurityStampAsync(user.Id));
                    Success(await manager.UpdateAsync(user));
                }
                else
                { Success(await manager.RemovePasswordAsync(user.Id)); Success(await manager.AddPasswordAsync(user.Id, password)); }
                using (var transaction = SqliteDatabase.BeginWriteTransaction(connection))
                {
                    permissions.Demand(actor, "User.Manage", connection, transaction);
                    // A fresh store avoids the pending prepared object when checking stale changes after waiting for writer.
                    using (var fresh = new SqliteIdentityStore(connection) { Transaction = transaction })
                        DemandOriginal(await fresh.FindByIdAsync(original.UserId), original);
                    store.Transaction = transaction; DemandUnique(connection, transaction, user.UserName, user.Id);
                    store.SaveManagedChanges(permissions, actor); DemandAdminRemains(connection, transaction);
                    AuditService.WriteStaff(connection, transaction, actor.UserId, password == null ? "User.Update" : "User.ResetPassword", "ApplicationUser", user.Id,
                        password == null ? "Cập nhật tài khoản " + user.UserName + "; role " + edit.Role + "; active " + edit.IsActive + "." : "Admin đặt lại mật khẩu tài khoản " + user.UserName + ".",
                        clock.UtcNow.ToUniversalTime());
                    transaction.Commit(); return Project(user);
                }
            }
        }
        private static AccountEdit Validate(AccountEdit input)
        {
            if (input == null) throw new ArgumentException("Cần nhập thông tin tài khoản.");
            var name = (input.UserName ?? "").Trim(); var full = (input.FullName ?? "").Trim();
            if (!Regex.IsMatch(name, @"\A[A-Za-z0-9._-]{3,100}\z")) throw new ArgumentException("Tên đăng nhập từ 3–100 ký tự chữ không dấu, số, chấm, gạch dưới/gạch ngang.");
            if (full.Length < 1 || full.Length > 100) throw new ArgumentException("Họ tên từ 1–100 ký tự.");
            if (input.Role != "Staff" && input.Role != "Manager" && input.Role != "Admin") throw new ArgumentException("Chỉ gán Staff, Manager hoặc Admin.");
            return new AccountEdit { UserName = name, FullName = full, Role = input.Role, IsActive = input.IsActive };
        }
        private static void ValidatePassword(string value) { if (value == null || value.Length < 8 || value.Length > 128) throw new ArgumentException("Mật khẩu từ 8–128 ký tự."); }
        private static void Success(IdentityResult result) { if (!result.Succeeded) throw new ArgumentException("Không lưu được tài khoản. Kiểm tra tên đăng nhập và mật khẩu."); }
        private static void DemandOriginal(ApplicationUser user, UserAccount original)
        { if (user == null || Project(user).Revision != original.Revision) throw new InvalidOperationException("Tài khoản đã thay đổi. Hãy tải lại trước khi lưu."); }
        private static UserAccount Project(ApplicationUser user)
        {
            var role = user.Roles.Single().RoleId;
            string revision;
            using (var hash = SHA256.Create()) revision = Convert.ToBase64String(hash.ComputeHash(Encoding.UTF8.GetBytes(
                string.Join("\u001f", user.Id, user.UserName, user.FullName, role, user.IsActive.ToString(), user.SecurityStamp, user.PasswordHash))));
            return new UserAccount { UserId = user.Id, UserName = user.UserName, FullName = user.FullName, Role = role, IsActive = user.IsActive, Revision = revision };
        }
        private static void DemandUnique(SQLiteConnection connection, SQLiteTransaction transaction, string name, string id)
        {
            using (var command = connection.CreateCommand())
            { command.Transaction = transaction; command.CommandText = "SELECT COUNT(*) FROM AspNetUsers WHERE NormalizedUserName=@name AND Id<>@id;";
                command.Parameters.AddWithValue("@name", SqliteIdentityStore.NormalizeName(name)); command.Parameters.AddWithValue("@id", id);
                if (Convert.ToInt64(command.ExecuteScalar()) > 0) throw new InvalidOperationException("Tên đăng nhập đã được sử dụng."); }
        }
        private static void DemandAdminRemains(SQLiteConnection connection, SQLiteTransaction transaction)
        {
            using (var command = connection.CreateCommand())
            { command.Transaction = transaction; command.CommandText = "SELECT COUNT(*) FROM AspNetUsers u JOIN AspNetUserRoles r ON r.UserId=u.Id WHERE u.IsActive=1 AND r.RoleId='Admin';";
                if (Convert.ToInt64(command.ExecuteScalar()) == 0) throw new InvalidOperationException("Phải còn ít nhất một Admin đang hoạt động."); }
        }
    }
}
