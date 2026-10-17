using System;
using System.Collections.Generic;
using System.Data;
using MySqlConnector;
using System.Globalization;
using MusicBoxManagement.Wpf.Data;
using MusicBoxManagement.Wpf.Models;

namespace MusicBoxManagement.Wpf.Services
{
    public sealed class MySqlRoomTypeService
    {
        private readonly MySqlDatabase database;

        public MySqlRoomTypeService(MySqlDatabase database)
        {
            this.database = database;
        }

        public List<RoomType> List()
        {
            var items = new List<RoomType>();
            using (var connection = database.OpenConnection())
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT RoomTypeId, Code, Name, Capacity, PricePerHour, Amenities, Description FROM RoomTypes ORDER BY RoomTypeId;";
                using (var reader = command.ExecuteReader())
                    while (reader.Read())
                        items.Add(ReadRoomType(reader));
            }
            return items;
        }

        public RoomType GetForEdit(LoginSession session, int roomTypeId)
        {
            using (var connection = database.OpenConnection())
            using (var transaction = MySqlDatabase.BeginReadTransaction(connection))
            {
                if (!new MySqlPermissionService(database).ReadAccess(session, connection, transaction).Permissions.ContainsKey("RoomType.Edit"))
                    throw new UnauthorizedAccessException("Bạn không có quyền sửa loại phòng.");
                return Find(connection, transaction, roomTypeId);
            }
        }

        public void Update(LoginSession session, RoomType original, RoomTypeEdit changes)
        {
            try
            {
                using (var connection = database.OpenConnection())
                using (var transaction = MySqlDatabase.BeginWriteTransaction(connection))
                {
                    new MySqlPermissionService(database).Demand(session, "RoomType.Edit", connection, transaction);
                    if (original == null || changes == null) throw new ArgumentException("Cần chọn loại phòng và nhập thông tin sửa.");
                    var name = (changes.Name ?? "").Trim();
                    var amenities = (changes.Amenities ?? "").Trim();
                    var description = string.IsNullOrWhiteSpace(changes.Description) ? null : changes.Description.Trim();
                    if (name.Length < 1 || name.Length > 100) throw new ArgumentException("Tên loại phòng từ 1–100 ký tự.");
                    if (changes.Capacity <= 0) throw new ArgumentException("Sức chứa phải là số nguyên lớn hơn 0.");
                    if (changes.PricePerHour <= 0) throw new ArgumentException("Giá mỗi giờ phải là số nguyên đồng lớn hơn 0.");
                    if (amenities.Length < 1 || amenities.Length > 1000) throw new ArgumentException("Tiện ích bắt buộc, tối đa 1000 ký tự.");
                    if (description != null && description.Length > 2000) throw new ArgumentException("Mô tả tối đa 2000 ký tự.");
                    var current = Find(connection, transaction, original.RoomTypeId, true);
                    if (current.Code != original.Code || current.Name != original.Name || current.Capacity != original.Capacity ||
                        current.PricePerHour != original.PricePerHour || current.Amenities != original.Amenities || current.Description != original.Description)
                        throw new InvalidOperationException("Loại phòng đã được người khác sửa. Hãy đóng cửa sổ, làm mới và mở lại để sửa.");
                    if (current.Name == name && current.Capacity == changes.Capacity && current.PricePerHour == changes.PricePerHour &&
                        current.Amenities == amenities && current.Description == description) return;
                    using (var command = connection.CreateCommand())
                    {
                        command.Transaction = transaction;
                        command.CommandText = @"UPDATE RoomTypes SET Name = @name, Capacity = @capacity,
    PricePerHour = @price, Amenities = @amenities, Description = @description WHERE RoomTypeId = @id;";
                        command.Parameters.AddWithValue("@name", name);
                        command.Parameters.AddWithValue("@capacity", changes.Capacity);
                        command.Parameters.AddWithValue("@price", changes.PricePerHour);
                        command.Parameters.AddWithValue("@amenities", amenities);
                        command.Parameters.AddWithValue("@description", (object)description ?? DBNull.Value);
                        command.Parameters.AddWithValue("@id", current.RoomTypeId);
                        command.ExecuteNonQuery();
                    }
                    MySqlAuditService.WriteStaff(connection, transaction, session.UserId, "RoomType.Update", "RoomType",
                        current.RoomTypeId.ToString(CultureInfo.InvariantCulture), "Cập nhật loại phòng " + current.Code + ".");
                    transaction.Commit();
                }
            }
            catch (MySqlException ex) when (ex.Number == 1213 || ex.Number == 1205)
            {
                throw new InvalidOperationException("Dữ liệu đang bận hoặc đã thay đổi. Hãy tải lại loại phòng trước khi lưu.", ex);
            }
        }

        private static RoomType Find(MySqlConnection connection, MySqlTransaction transaction, int id, bool forUpdate = false)
        {
            using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = "SELECT RoomTypeId, Code, Name, Capacity, PricePerHour, Amenities, Description FROM RoomTypes WHERE RoomTypeId = @id" + (forUpdate ? " FOR UPDATE;" : ";");
                command.Parameters.AddWithValue("@id", id);
                using (var reader = command.ExecuteReader())
                {
                    if (!reader.Read()) throw new InvalidOperationException("Không tìm thấy loại phòng đã chọn.");
                    return ReadRoomType(reader);
                }
            }
        }

        private static RoomType ReadRoomType(MySqlDataReader reader) => new RoomType
        {
            RoomTypeId = reader.GetInt32(0), Code = reader.GetString(1), Name = reader.GetString(2),
            Capacity = reader.GetInt32(3), PricePerHour = reader.GetInt64(4), Amenities = reader.GetString(5),
            Description = reader.IsDBNull(6) ? null : reader.GetString(6)
        };
    }
}
