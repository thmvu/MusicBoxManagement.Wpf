using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SQLite;
using System.Linq;
using MusicBoxManagement.Wpf.Data;
using MusicBoxManagement.Wpf.Models;

namespace MusicBoxManagement.Wpf.Services
{
    public sealed class StaffAccess
    {
        public string FullName { get; internal set; }
        public string Role { get; internal set; }
        public IReadOnlyDictionary<string, string> Permissions { get; internal set; }
    }

    public sealed class PermissionService
    {
        private readonly SqliteDatabase database;
        public PermissionService(SqliteDatabase database) { this.database = database; }

        public StaffAccess GetStaffAccess(LoginSession session)
        {
            using (var connection = database.OpenConnection())
            // One read snapshot, so user, role and permissions agree during this call.
            using (var transaction = connection.BeginTransaction(IsolationLevel.ReadCommitted))
                return ReadAccess(session, connection, transaction);
        }

        public bool HasPermission(LoginSession session, string code)
        {
            if (!PermissionCatalog.All.ContainsKey(code ?? "")) return false;
            try { return GetStaffAccess(session).Permissions.ContainsKey(code); }
            catch (UnauthorizedAccessException) { return false; }
        }

        public void Demand(LoginSession session, string code)
        {
            if (!HasPermission(session, code)) throw new UnauthorizedAccessException("Bạn không có quyền thực hiện thao tác này.");
        }

        // Future write services call this overload with their existing transaction.
        public void Demand(LoginSession session, string code, SQLiteConnection connection, SQLiteTransaction transaction)
        {
            if (transaction == null || transaction.Connection != connection)
                throw new ArgumentException("Cần cùng kết nối và transaction nghiệp vụ.");
            var access = ReadAccess(session, connection, transaction);
            if (code == null || !access.Permissions.ContainsKey(code))
                throw new UnauthorizedAccessException("Bạn không có quyền thực hiện thao tác này.");
        }

        internal StaffAccess ReadAccess(LoginSession session, SQLiteConnection connection, SQLiteTransaction transaction)
        {
            if (session == null || session.IsSignedOut) throw new UnauthorizedAccessException("Cần đăng nhập khu vực nhân viên.");
            var access = new StaffAccess();
            string roleId;
            using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = @"SELECT u.FullName, u.IsActive, u.SecurityStamp, r.Id, r.Name
FROM AspNetUsers u JOIN AspNetUserRoles ur ON ur.UserId = u.Id
JOIN AspNetRoles r ON r.Id = ur.RoleId WHERE u.Id = @id;";
                command.Parameters.AddWithValue("@id", session.UserId);
                using (var reader = command.ExecuteReader())
                {
                    if (!reader.Read() || reader.GetInt32(1) != 1 || reader.GetString(2) != session.SecurityStamp)
                        throw new UnauthorizedAccessException("Phiên đăng nhập đã hết hiệu lực. Hãy đăng nhập lại.");
                    access.FullName = reader.GetString(0);
                    roleId = reader.GetString(3);
                    access.Role = reader.GetString(4);
                }
            }
            if (access.Role == "Admin") { access.Permissions = PermissionCatalog.All; return access; }
            var granted = new HashSet<string>(StringComparer.Ordinal);
            using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = @"SELECT p.Code FROM Permission p
JOIN RolePermission rp ON rp.PermissionId = p.PermissionId WHERE rp.RoleId = @role;";
                command.Parameters.AddWithValue("@role", roleId);
                using (var reader = command.ExecuteReader())
                    while (reader.Read()) granted.Add(reader.GetString(0));
            }
            granted.RemoveWhere(code => !PermissionCatalog.All.ContainsKey(code) || PermissionCatalog.IsAdministrative(code) ||
                (access.Role == "Staff" && code.StartsWith("Report.", StringComparison.Ordinal)));
            if (!granted.Contains("Report.View")) granted.Remove("Report.Export");
            if (!granted.Contains("Invoice.View")) granted.Remove("Invoice.Print");
            access.Permissions = new System.Collections.ObjectModel.ReadOnlyDictionary<string, string>(
                PermissionCatalog.All.Where(p => granted.Contains(p.Key)).ToDictionary(p => p.Key, p => p.Value));
            return access;
        }
    }
}
