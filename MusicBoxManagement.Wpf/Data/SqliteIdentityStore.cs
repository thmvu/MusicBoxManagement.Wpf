using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNet.Identity;
using Microsoft.AspNet.Identity.EntityFramework;
using MusicBoxManagement.Wpf.Models;

namespace MusicBoxManagement.Wpf.Data
{
    // Like an Identity store with AutoSaveChanges=false: UserManager prepares
    // changes, then the service saves them in its short business transaction.
    // Connection and transaction belong to the service, not this store.
    internal sealed class SqliteIdentityStore : IUserStore<ApplicationUser>, IUserPasswordStore<ApplicationUser>,
        IUserRoleStore<ApplicationUser>, IUserSecurityStampStore<ApplicationUser>, IRoleStore<IdentityRole>
    {
        private readonly SQLiteConnection connection;
        private readonly Dictionary<string, ApplicationUser> pending = new Dictionary<string, ApplicationUser>();
        private readonly HashSet<string> newIds = new HashSet<string>();
        internal SQLiteTransaction Transaction { get; set; }

        internal SqliteIdentityStore(SQLiteConnection connection) { this.connection = connection; }

        private SQLiteCommand Command(string sql, params object[] values)
        {
            var command = connection.CreateCommand();
            command.Transaction = Transaction;
            command.CommandText = sql;
            for (var i = 0; i < values.Length; i += 2)
                command.Parameters.AddWithValue((string)values[i], values[i + 1] ?? DBNull.Value);
            return command;
        }

        internal static string NormalizeName(string name) => (name ?? "").Trim().ToUpperInvariant();

        public Task<ApplicationUser> FindByIdAsync(string userId) => pending.TryGetValue(userId, out var user)
            ? Task.FromResult(user) : FindUser("u.Id = @value", userId);
        public Task<ApplicationUser> FindByNameAsync(string userName) =>
            FindUser("u.NormalizedUserName = @value", NormalizeName(userName));

        private Task<ApplicationUser> FindUser(string condition, string value)
        {
            using (var command = Command(@"SELECT u.Id, u.UserName, u.PasswordHash, u.SecurityStamp,
u.FullName, u.IsActive, ur.RoleId FROM AspNetUsers u
LEFT JOIN AspNetUserRoles ur ON ur.UserId = u.Id WHERE " + condition, "@value", value))
            using (var reader = command.ExecuteReader())
            {
                if (!reader.Read()) return Task.FromResult<ApplicationUser>(null);
                var user = new ApplicationUser
                {
                    Id = reader.GetString(0), UserName = reader.GetString(1), PasswordHash = reader.GetString(2),
                    SecurityStamp = reader.GetString(3), FullName = reader.GetString(4), IsActive = reader.GetInt32(5) == 1
                };
                if (!reader.IsDBNull(6)) user.Roles.Add(new IdentityUserRole { UserId = user.Id, RoleId = reader.GetString(6) });
                return Task.FromResult(user);
            }
        }

        public Task CreateAsync(ApplicationUser user)
        {
            pending[user.Id] = user;
            newIds.Add(user.Id);
            return Task.CompletedTask;
        }

        public Task UpdateAsync(ApplicationUser user)
        {
            pending[user.Id] = user;
            return Task.CompletedTask;
        }

        internal void SaveChanges()
        {
            if (Transaction == null) throw new InvalidOperationException("Lưu tài khoản cần transaction ghi.");
            foreach (var user in pending.Values)
            {
                if (user.Roles.Count != 1) throw new InvalidOperationException("Tài khoản phải có đúng một role.");
                // The current milestone creates only the initial Admin. Updates
                // need the account-management service and last-Admin rules later.
                if (!newIds.Contains(user.Id)) throw new NotSupportedException("Chưa triển khai cập nhật tài khoản.");
                using (var command = Command(@"INSERT INTO AspNetUsers
(Id, UserName, NormalizedUserName, PasswordHash, SecurityStamp, FullName, IsActive)
VALUES (@id, @name, @normalized, @hash, @stamp, @fullName, @active);",
                    "@id", user.Id, "@name", user.UserName, "@normalized", NormalizeName(user.UserName),
                    "@hash", user.PasswordHash, "@stamp", user.SecurityStamp,
                    "@fullName", user.FullName, "@active", user.IsActive ? 1 : 0)) command.ExecuteNonQuery();
                using (var command = Command("INSERT INTO AspNetUserRoles(UserId, RoleId) VALUES (@user, @role);",
                    "@user", user.Id, "@role", user.Roles.Single().RoleId)) command.ExecuteNonQuery();
            }
            pending.Clear();
            newIds.Clear();
        }

        public Task DeleteAsync(ApplicationUser user) => throw new NotSupportedException("Không xóa tài khoản có lịch sử.");
        public Task SetPasswordHashAsync(ApplicationUser user, string passwordHash)
        { user.PasswordHash = passwordHash; return Task.CompletedTask; }
        public Task<string> GetPasswordHashAsync(ApplicationUser user) => Task.FromResult(user.PasswordHash);
        public Task<bool> HasPasswordAsync(ApplicationUser user) => Task.FromResult(!string.IsNullOrEmpty(user.PasswordHash));
        public Task SetSecurityStampAsync(ApplicationUser user, string stamp)
        { user.SecurityStamp = stamp; return Task.CompletedTask; }
        public Task<string> GetSecurityStampAsync(ApplicationUser user) => Task.FromResult(user.SecurityStamp);

        public async Task AddToRoleAsync(ApplicationUser user, string roleName)
        {
            var role = await FindByNameAsyncRole(roleName);
            if (role == null) throw new InvalidOperationException("Role không hợp lệ.");
            if (user.Roles.Count != 0) throw new InvalidOperationException("Tài khoản đã có role.");
            user.Roles.Add(new IdentityUserRole { UserId = user.Id, RoleId = role.Id });
            user.SecurityStamp = Guid.NewGuid().ToString("N");
        }
        public async Task RemoveFromRoleAsync(ApplicationUser user, string roleName)
        {
            var role = await FindByNameAsyncRole(roleName);
            var membership = user.Roles.SingleOrDefault(r => r.RoleId == role?.Id);
            if (membership != null) user.Roles.Remove(membership);
            user.SecurityStamp = Guid.NewGuid().ToString("N");
        }
        public Task<IList<string>> GetRolesAsync(ApplicationUser user)
        {
            var roles = new List<string>();
            foreach (var membership in user.Roles)
                using (var command = Command("SELECT Name FROM AspNetRoles WHERE Id = @id;", "@id", membership.RoleId))
                {
                    var name = command.ExecuteScalar() as string;
                    if (name != null) roles.Add(name);
                }
            return Task.FromResult<IList<string>>(roles);
        }
        public async Task<bool> IsInRoleAsync(ApplicationUser user, string roleName) =>
            (await GetRolesAsync(user)).Contains(roleName, StringComparer.OrdinalIgnoreCase);

        private Task<IdentityRole> FindRole(string condition, string value)
        {
            using (var command = Command("SELECT Id, Name FROM AspNetRoles WHERE " + condition, "@value", value))
            using (var reader = command.ExecuteReader())
                return Task.FromResult(reader.Read() ? new IdentityRole { Id = reader.GetString(0), Name = reader.GetString(1) } : null);
        }
        private Task<IdentityRole> FindByNameAsyncRole(string roleName) => FindRole("Name = @value COLLATE NOCASE", roleName);
        Task<IdentityRole> IRoleStore<IdentityRole, string>.FindByIdAsync(string id) => FindRole("Id = @value", id);
        Task<IdentityRole> IRoleStore<IdentityRole, string>.FindByNameAsync(string name) => FindByNameAsyncRole(name);
        Task IRoleStore<IdentityRole, string>.CreateAsync(IdentityRole role) => throw new NotSupportedException("Role được seed cố định.");
        Task IRoleStore<IdentityRole, string>.UpdateAsync(IdentityRole role) => throw new NotSupportedException("Không đổi role cố định.");
        Task IRoleStore<IdentityRole, string>.DeleteAsync(IdentityRole role) => throw new NotSupportedException("Không xóa role cố định.");
        public void Dispose() { }
    }
}
