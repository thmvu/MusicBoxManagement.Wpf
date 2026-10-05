using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SQLite;
using System.Globalization;
using MusicBoxManagement.Wpf.Data;
using MusicBoxManagement.Wpf.Models;

namespace MusicBoxManagement.Wpf.Services
{
    public sealed class CustomerService
    {
        private readonly SqliteDatabase database;
        private readonly PermissionService permissions;
        public CustomerService(SqliteDatabase database) { this.database = database; permissions = new PermissionService(database); }

        public CustomerList Search(LoginSession session, string nameQuery = null, string phoneQuery = null)
        {
            using (var connection = database.OpenConnection())
            using (var transaction = connection.BeginTransaction(IsolationLevel.ReadCommitted))
            using (var command = connection.CreateCommand())
            {
                var access = permissions.ReadAccess(session, connection, transaction);
                if (!access.Permissions.ContainsKey("Customer.View")) throw new UnauthorizedAccessException("Cần quyền xem khách hàng.");
                var name = (nameQuery ?? "").Trim();
                var phone = string.IsNullOrWhiteSpace(phoneQuery) ? null : PhoneNumberNormalizer.Normalize(phoneQuery);
                command.Transaction = transaction;
                command.CommandText = "SELECT CustomerId,FullName,PhoneNumber FROM Customers WHERE (@phone IS NULL OR PhoneNumber=@phone) ORDER BY CustomerId;";
                command.Parameters.AddWithValue("@phone", (object)phone ?? DBNull.Value);
                var items = new List<Customer>();
                using (var reader = command.ExecuteReader())
                    while (reader.Read())
                    {
                        var item = Read(reader);
                        // .NET handles Vietnamese upper/lower case, unlike SQLite's built-in lower().
                        if (item.FullName.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0) items.Add(item);
                    }
                return new CustomerList { Items = items, CanCreate = access.Permissions.ContainsKey("Customer.Create"), CanEdit = access.Permissions.ContainsKey("Customer.Edit") };
            }
        }

        public Customer Save(LoginSession session, Customer original, CustomerEdit input)
        {
            using (var connection = database.OpenConnection())
            using (var transaction = SqliteDatabase.BeginWriteTransaction(connection))
            using (var command = connection.CreateCommand())
            {
                permissions.Demand(session, "Customer.View", connection, transaction);
                permissions.Demand(session, original == null ? "Customer.Create" : "Customer.Edit", connection, transaction);
                if (input == null) throw new ArgumentException("Cần nhập thông tin khách hàng.");
                var name = (input.FullName ?? "").Trim();
                if (name.Length < 1 || name.Length > 100) throw new ArgumentException("Họ tên từ 1–100 ký tự.");
                var phone = PhoneNumberNormalizer.Normalize(input.PhoneNumber);
                command.Transaction = transaction;
                var id = original?.CustomerId ?? 0;
                if (original != null)
                {
                    command.CommandText = "SELECT CustomerId,FullName,PhoneNumber FROM Customers WHERE CustomerId=@id;";
                    command.Parameters.AddWithValue("@id", id);
                    Customer current;
                    using (var reader = command.ExecuteReader())
                    {
                        if (!reader.Read()) throw new InvalidOperationException("Không tìm thấy khách hàng đã chọn.");
                        current = Read(reader);
                    }
                    if (current.FullName != original.FullName || current.PhoneNumber != original.PhoneNumber)
                        throw new InvalidOperationException("Khách hàng đã thay đổi. Hãy tìm lại rồi chọn để sửa.");
                    if (current.FullName == name && current.PhoneNumber == phone) return current;
                }
                command.CommandText = "SELECT COUNT(*) FROM Customers WHERE PhoneNumber=@phone AND CustomerId<>@current;";
                command.Parameters.AddWithValue("@phone", phone); command.Parameters.AddWithValue("@current", id);
                if (Convert.ToInt64(command.ExecuteScalar()) > 0)
                    throw new ArgumentException("SĐT đã thuộc một khách hàng khác. Hãy tìm và dùng khách hàng hiện có.");
                command.Parameters.Clear();
                command.Parameters.AddWithValue("@name", name); command.Parameters.AddWithValue("@phone", phone);
                command.Parameters.AddWithValue("@id", id);
                command.CommandText = original == null
                    ? "INSERT INTO Customers(FullName,PhoneNumber) VALUES(@name,@phone); SELECT last_insert_rowid();"
                    : "UPDATE Customers SET FullName=@name,PhoneNumber=@phone WHERE CustomerId=@id;";
                if (original == null) id = Convert.ToInt32(command.ExecuteScalar()); else command.ExecuteNonQuery();
                AuditService.WriteStaff(connection, transaction, session.UserId, original == null ? "Customer.Create" : "Customer.Update", "Customer",
                    id.ToString(CultureInfo.InvariantCulture), "Lưu khách hàng " + name + ", SĐT " + phone + ".");
                transaction.Commit();
                return new Customer { CustomerId = id, FullName = name, PhoneNumber = phone };
            }
        }
        private static Customer Read(SQLiteDataReader reader) => new Customer { CustomerId = reader.GetInt32(0), FullName = reader.GetString(1), PhoneNumber = reader.GetString(2) };
    }
}
