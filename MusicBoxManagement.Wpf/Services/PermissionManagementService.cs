using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SQLite;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using MusicBoxManagement.Wpf.Data;
using MusicBoxManagement.Wpf.Models;

namespace MusicBoxManagement.Wpf.Services
{
    public sealed class PermissionManagementService
    {
        private readonly SqliteDatabase database;
        private readonly PermissionService permissions;
        private readonly IClock clock;

        public PermissionManagementService(SqliteDatabase database) : this(database, new SystemClock()) { }
        public PermissionManagementService(SqliteDatabase database, IClock clock)
        {
            this.database = database ?? throw new ArgumentNullException(nameof(database));
            this.clock = clock ?? throw new ArgumentNullException(nameof(clock));
            permissions = new PermissionService(database);
        }

        public RolePermissionMatrix Read(LoginSession actor, string role)
        {
            using (var connection = database.OpenConnection())
            using (var transaction = connection.BeginTransaction(IsolationLevel.ReadCommitted))
            {
                permissions.Demand(actor, "Permission.Manage", connection, transaction);
                ValidateRole(role);
                return ReadMatrix(connection, transaction, role);
            }
        }

        public RolePermissionMatrix Update(LoginSession actor, RolePermissionMatrix original, IEnumerable<string> selectedCodes)
        {
            using (var connection = database.OpenConnection())
            using (var transaction = SqliteDatabase.BeginWriteTransaction(connection))
            {
                permissions.Demand(actor, "Permission.Manage", connection, transaction);
                if (original == null) throw new ArgumentException("Cần tải ma trận quyền trước khi sửa.");
                ValidateRole(original.Role);
                var selected = new HashSet<string>(ValidateSelection(original.Role, selectedCodes), StringComparer.Ordinal);
                var current = ReadMatrix(connection, transaction, original.Role);
                if (current.Revision != original.Revision)
                    throw new InvalidOperationException("Ma trận quyền đã thay đổi. Hãy tải lại trước khi sửa.");

                // Compare raw stored grants as well: saving a sanitized legacy matrix cleans invalid grants.
                var raw = ReadRaw(connection, transaction, original.Role);
                if (raw.SetEquals(selected)) return current;
                using (var command = connection.CreateCommand())
                {
                    command.Transaction = transaction;
                    command.CommandText = "DELETE FROM RolePermission WHERE RoleId=(SELECT Id FROM AspNetRoles WHERE Name=@role);";
                    command.Parameters.AddWithValue("@role", original.Role);
                    command.ExecuteNonQuery();
                    foreach (var code in selected.OrderBy(c => c, StringComparer.Ordinal))
                    {
                        command.CommandText = @"INSERT INTO RolePermission(RoleId,PermissionId)
SELECT r.Id,p.PermissionId FROM AspNetRoles r CROSS JOIN Permission p WHERE r.Name=@role AND p.Code=@code;";
                        command.Parameters.AddWithValue("@code", code);
                        if (command.ExecuteNonQuery() != 1)
                            throw new InvalidOperationException("Danh mục quyền không đầy đủ. Hãy kiểm tra dữ liệu trước khi lưu.");
                        command.Parameters.RemoveAt("@code");
                    }
                }
                AuditService.WriteStaff(connection, transaction, actor.UserId, "Permission.Update", "RolePermission", original.Role,
                    "Cập nhật quyền " + original.Role + "; trước: " + string.Join(", ", raw.OrderBy(c => c, StringComparer.Ordinal)) +
                    "; sau: " + string.Join(", ", selected.OrderBy(c => c, StringComparer.Ordinal)) + ".", clock.UtcNow.ToUniversalTime());
                var result = ReadMatrix(connection, transaction, original.Role);
                transaction.Commit();
                return result;
            }
        }

        private static void ValidateRole(string role)
        {
            if (role != "Staff" && role != "Manager")
                throw new ArgumentException("Chỉ chỉnh quyền Staff hoặc Manager. Admin luôn toàn quyền cố định.");
        }

        public static IReadOnlyList<string> ValidateSelection(string role, IEnumerable<string> codes)
        {
            ValidateRole(role);
            if (codes == null) throw new ArgumentException("Cần chọn danh sách quyền.");
            var result = new HashSet<string>(StringComparer.Ordinal);
            foreach (var code in codes)
            {
                if (code == null || !PermissionCatalog.All.ContainsKey(code) || PermissionCatalog.IsAdministrative(code) ||
                    (role == "Staff" && code.StartsWith("Report.", StringComparison.Ordinal)))
                    throw new ArgumentException("Quyền không hợp lệ hoặc không được cấp cho vai trò này.");
                result.Add(code);
            }
            if ((result.Contains("Invoice.Print") && !result.Contains("Invoice.View")) ||
                (result.Contains("Report.Export") && !result.Contains("Report.View")))
                throw new ArgumentException("Quyền in cần xem hóa đơn; xuất báo cáo cần xem báo cáo.");
            return result.OrderBy(c => c, StringComparer.Ordinal).ToList().AsReadOnly();
        }

        private static RolePermissionMatrix ReadMatrix(SQLiteConnection connection, SQLiteTransaction transaction, string role)
        {
            var raw = ReadRaw(connection, transaction, role);
            string revision;
            // Length prefixes avoid ambiguous concatenation, including unexpected legacy codes.
            var source = role + ":" + string.Concat(raw.OrderBy(c => c, StringComparer.Ordinal).Select(c => c.Length + ":" + c));
            using (var sha = SHA256.Create()) revision = Convert.ToBase64String(sha.ComputeHash(Encoding.UTF8.GetBytes(source)));
            var effective = new HashSet<string>(raw.Where(c => PermissionCatalog.All.ContainsKey(c) &&
                !PermissionCatalog.IsAdministrative(c) && !(role == "Staff" && c.StartsWith("Report.", StringComparison.Ordinal))), StringComparer.Ordinal);
            if (!effective.Contains("Invoice.View")) effective.Remove("Invoice.Print");
            if (!effective.Contains("Report.View")) effective.Remove("Report.Export");
            return new RolePermissionMatrix(role, revision, effective.OrderBy(c => c, StringComparer.Ordinal));
        }

        private static HashSet<string> ReadRaw(SQLiteConnection connection, SQLiteTransaction transaction, string role)
        {
            var result = new HashSet<string>(StringComparer.Ordinal);
            using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = @"SELECT p.Code FROM RolePermission rp JOIN Permission p ON p.PermissionId=rp.PermissionId
JOIN AspNetRoles r ON r.Id=rp.RoleId WHERE r.Name=@role;";
                command.Parameters.AddWithValue("@role", role);
                using (var reader = command.ExecuteReader()) while (reader.Read()) result.Add(reader.GetString(0));
            }
            return result;
        }
    }
}
