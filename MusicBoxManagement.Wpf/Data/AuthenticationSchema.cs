using System.Data.SQLite;
using MusicBoxManagement.Wpf.Services;

namespace MusicBoxManagement.Wpf.Data
{
    internal static class AuthenticationSchema
    {
        internal static void Create(SQLiteCommand command)
        {
            command.Parameters.Clear();
            command.CommandText = @"
CREATE TABLE AspNetUsers (
    Id TEXT PRIMARY KEY NOT NULL,
    UserName TEXT NOT NULL CHECK(length(trim(UserName)) BETWEEN 1 AND 100),
    NormalizedUserName TEXT NOT NULL UNIQUE,
    PasswordHash TEXT NOT NULL CHECK(length(PasswordHash) > 0),
    SecurityStamp TEXT NOT NULL CHECK(length(SecurityStamp) > 0),
    FullName TEXT NOT NULL CHECK(length(trim(FullName)) BETWEEN 1 AND 100),
    IsActive INTEGER NOT NULL CHECK(IsActive IN (0, 1))
);
CREATE TABLE AspNetRoles (
    Id TEXT PRIMARY KEY NOT NULL,
    Name TEXT NOT NULL UNIQUE CHECK(Name IN ('Staff', 'Manager', 'Admin'))
);
CREATE TABLE AspNetUserRoles (
    UserId TEXT PRIMARY KEY NOT NULL REFERENCES AspNetUsers(Id),
    RoleId TEXT NOT NULL REFERENCES AspNetRoles(Id)
);
CREATE INDEX IX_AspNetUserRoles_RoleId ON AspNetUserRoles(RoleId);
CREATE TABLE Permission (
    PermissionId INTEGER PRIMARY KEY,
    Code TEXT NOT NULL UNIQUE,
    Name TEXT NOT NULL
);
CREATE TABLE RolePermission (
    RoleId TEXT NOT NULL REFERENCES AspNetRoles(Id),
    PermissionId INTEGER NOT NULL REFERENCES Permission(PermissionId),
    PRIMARY KEY(RoleId, PermissionId)
);
CREATE TABLE AuditLog (
    AuditLogId INTEGER PRIMARY KEY,
    ActorType TEXT NOT NULL CHECK(ActorType IN ('User', 'Guest', 'System')),
    UserId TEXT NULL REFERENCES AspNetUsers(Id),
    Action TEXT NOT NULL,
    EntityName TEXT NOT NULL,
    EntityId TEXT NULL,
    Description TEXT NOT NULL,
    CreatedAt TEXT NOT NULL
);
INSERT INTO AspNetRoles(Id, Name) VALUES ('Staff', 'Staff'), ('Manager', 'Manager'), ('Admin', 'Admin');";
            command.ExecuteNonQuery();
            foreach (var permission in PermissionCatalog.All)
            {
                command.CommandText = "INSERT INTO Permission(Code, Name) VALUES (@code, @name);";
                command.Parameters.Clear();
                command.Parameters.AddWithValue("@code", permission.Key);
                command.Parameters.AddWithValue("@name", permission.Value);
                command.ExecuteNonQuery();
            }
            foreach (var role in new[] { "Staff", "Manager" })
                foreach (var code in PermissionCatalog.Defaults(role))
                {
                    command.CommandText = @"INSERT INTO RolePermission(RoleId, PermissionId)
SELECT @role, PermissionId FROM Permission WHERE Code = @code;";
                    command.Parameters.Clear();
                    command.Parameters.AddWithValue("@role", role);
                    command.Parameters.AddWithValue("@code", code);
                    command.ExecuteNonQuery();
                }
            command.Parameters.Clear();
            command.CommandText = "PRAGMA user_version = 2;";
            command.ExecuteNonQuery();
        }
    }
}
