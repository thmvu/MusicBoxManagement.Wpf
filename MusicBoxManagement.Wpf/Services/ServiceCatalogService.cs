using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SQLite;
using System.Globalization;
using System.Linq;
using MusicBoxManagement.Wpf.Data;
using MusicBoxManagement.Wpf.Models;

namespace MusicBoxManagement.Wpf.Services
{
    public sealed class ServiceCatalogService
    {
        private readonly SqliteDatabase database;
        private readonly PermissionService permissions;
        public static IReadOnlyList<string> Categories { get; } = Array.AsReadOnly(new[] { "Đồ uống", "Đồ ăn", "Khác" });
        public ServiceCatalogService(SqliteDatabase database) { this.database = database; permissions = new PermissionService(database); }

        public List<ServiceItem> ListForManagement(LoginSession session)
        {
            using (var connection = database.OpenConnection())
            using (var transaction = connection.BeginTransaction(IsolationLevel.ReadCommitted))
            using (var command = connection.CreateCommand())
            {
                permissions.Demand(session, "Service.Manage", connection, transaction);
                command.Transaction = transaction;
                command.CommandText = "SELECT ServiceId,Name,Category,Price,Description,IsActive FROM Services ORDER BY ServiceId;";
                var items = new List<ServiceItem>();
                using (var reader = command.ExecuteReader()) while (reader.Read()) items.Add(Read(reader));
                return items;
            }
        }

        public int Save(LoginSession session, ServiceItem original, ServiceEdit input)
        {
            using (var connection = database.OpenConnection())
            using (var transaction = SqliteDatabase.BeginWriteTransaction(connection))
            using (var command = connection.CreateCommand())
            {
                permissions.Demand(session, "Service.Manage", connection, transaction);
                if (input == null) throw new ArgumentException("Cần nhập thông tin dịch vụ.");
                var name = (input.Name ?? "").Trim();
                var category = input.Category;
                var description = string.IsNullOrWhiteSpace(input.Description) ? null : input.Description.Trim();
                var price = input.Price;
                var active = input.IsActive;
                if (name.Length < 1 || name.Length > 100) throw new ArgumentException("Tên dịch vụ từ 1–100 ký tự.");
                if (!Categories.Contains(category)) throw new ArgumentException("Chọn nhóm Đồ uống, Đồ ăn hoặc Khác.");
                if (price <= 0) throw new ArgumentException("Giá dịch vụ phải là số nguyên đồng lớn hơn 0.");
                if (description != null && description.Length > 2000) throw new ArgumentException("Mô tả tối đa 2000 ký tự.");
                command.Transaction = transaction;
                var id = original?.ServiceId ?? 0;
                if (original != null)
                {
                    command.CommandText = "SELECT ServiceId,Name,Category,Price,Description,IsActive FROM Services WHERE ServiceId=@id;";
                    command.Parameters.AddWithValue("@id", id);
                    ServiceItem current;
                    using (var reader = command.ExecuteReader())
                    {
                        if (!reader.Read()) throw new InvalidOperationException("Không tìm thấy dịch vụ đã chọn.");
                        current = Read(reader);
                    }
                    if (current.Name != original.Name || current.Category != original.Category || current.Price != original.Price ||
                        current.Description != original.Description || current.IsActive != original.IsActive)
                        throw new InvalidOperationException("Dịch vụ đã thay đổi. Hãy làm mới rồi chọn lại để sửa.");
                    if (current.Name == name && current.Category == category && current.Price == price && current.Description == description && current.IsActive == active)
                        return id;
                }
                command.Parameters.AddWithValue("@name", name);
                command.Parameters.AddWithValue("@category", category);
                command.Parameters.AddWithValue("@price", price);
                command.Parameters.AddWithValue("@description", (object)description ?? DBNull.Value);
                command.Parameters.AddWithValue("@active", active ? 1 : 0);
                command.CommandText = original == null
                    ? "INSERT INTO Services(Name,Category,Price,Description,IsActive) VALUES(@name,@category,@price,@description,@active); SELECT last_insert_rowid();"
                    : "UPDATE Services SET Name=@name,Category=@category,Price=@price,Description=@description,IsActive=@active WHERE ServiceId=@id;";
                if (original == null) id = Convert.ToInt32(command.ExecuteScalar());
                else command.ExecuteNonQuery();
                AuditService.WriteStaff(connection, transaction, session.UserId, original == null ? "Service.Create" : "Service.Update", "Service",
                    id.ToString(CultureInfo.InvariantCulture), "Lưu dịch vụ " + name + (active ? " (đang bán)." : " (ngừng bán)."));
                transaction.Commit();
                return id;
            }
        }

        private static ServiceItem Read(SQLiteDataReader reader) => new ServiceItem {
            ServiceId = reader.GetInt32(0), Name = reader.GetString(1), Category = reader.GetString(2), Price = reader.GetInt64(3),
            Description = reader.IsDBNull(4) ? null : reader.GetString(4), IsActive = reader.GetInt32(5) == 1 };
    }
}
