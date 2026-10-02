using System.Collections.Generic;
using MusicBoxManagement.Wpf.Data;
using MusicBoxManagement.Wpf.Models;

namespace MusicBoxManagement.Wpf.Services
{
    public sealed class RoomTypeService
    {
        private readonly SqliteDatabase database;

        public RoomTypeService(SqliteDatabase database)
        {
            this.database = database;
        }

        public List<RoomType> List()
        {
            database.Initialize();
            var items = new List<RoomType>();
            using (var connection = database.OpenConnection())
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT RoomTypeId, Code, Name, Capacity, PricePerHour, Amenities, Description FROM RoomTypes ORDER BY RoomTypeId;";
                using (var reader = command.ExecuteReader())
                    while (reader.Read())
                        items.Add(new RoomType
                        {
                            RoomTypeId = reader.GetInt32(0),
                            Code = reader.GetString(1),
                            Name = reader.GetString(2),
                            Capacity = reader.GetInt32(3),
                            PricePerHour = reader.GetInt64(4),
                            Amenities = reader.GetString(5),
                            Description = reader.IsDBNull(6) ? null : reader.GetString(6)
                        });
            }
            return items;
        }
    }
}
