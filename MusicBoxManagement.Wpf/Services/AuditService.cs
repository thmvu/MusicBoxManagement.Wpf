using System;
using System.Data.SQLite;
using System.Globalization;

namespace MusicBoxManagement.Wpf.Services
{
    internal static class AuditService
    {
        internal static void WriteStaff(SQLiteConnection connection, SQLiteTransaction transaction,
            string userId, string action, string entityName, string entityId, string description, DateTimeOffset? occurredAt = null)
        { Write(connection, transaction, "Staff", userId, action, entityName, entityId, description, occurredAt ?? DateTimeOffset.UtcNow); }

        internal static void WriteGuest(SQLiteConnection connection, SQLiteTransaction transaction,
            string action, string entityName, string entityId, string description, DateTimeOffset occurredAt)
        { Write(connection, transaction, "Guest", null, action, entityName, entityId, description, occurredAt); }

        internal static void WriteSystem(SQLiteConnection connection, SQLiteTransaction transaction,
            string action, string entityName, string entityId, string description, DateTimeOffset occurredAt)
        { Write(connection, transaction, "System", null, action, entityName, entityId, description, occurredAt); }

        private static void Write(SQLiteConnection connection, SQLiteTransaction transaction, string actorType,
            string userId, string action, string entityName, string entityId, string description, DateTimeOffset occurredAt)
        {
            using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = @"INSERT INTO AuditLog
(ActorType, UserId, Action, EntityName, EntityId, Description, CreatedAt)
VALUES (@actor, @user, @action, @entity, @id, @description, @now);";
                command.Parameters.AddWithValue("@actor", actorType);
                command.Parameters.AddWithValue("@user", (object)userId ?? DBNull.Value);
                command.Parameters.AddWithValue("@action", action);
                command.Parameters.AddWithValue("@entity", entityName);
                command.Parameters.AddWithValue("@id", entityId);
                command.Parameters.AddWithValue("@description", description);
                command.Parameters.AddWithValue("@now", occurredAt.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
                command.ExecuteNonQuery();
            }
        }
    }
}
