using System;
using System.Data.SQLite;
using System.Globalization;

namespace MusicBoxManagement.Wpf.Services
{
    internal static class AuditService
    {
        internal static void WriteStaff(SQLiteConnection connection, SQLiteTransaction transaction,
            string userId, string action, string entityName, string entityId, string description)
        {
            using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = @"INSERT INTO AuditLog
(ActorType, UserId, Action, EntityName, EntityId, Description, CreatedAt)
VALUES ('Staff', @user, @action, @entity, @id, @description, @now);";
                command.Parameters.AddWithValue("@user", userId);
                command.Parameters.AddWithValue("@action", action);
                command.Parameters.AddWithValue("@entity", entityName);
                command.Parameters.AddWithValue("@id", entityId);
                command.Parameters.AddWithValue("@description", description);
                command.Parameters.AddWithValue("@now", DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture));
                command.ExecuteNonQuery();
            }
        }
    }
}
