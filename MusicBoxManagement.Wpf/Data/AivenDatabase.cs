using System;
using System.IO;
using System.Threading.Tasks;
using MySqlConnector;

namespace MusicBoxManagement.Wpf.Data
{
    public static class AivenDatabase
    {
        public static async Task<string> TestConnectionAsync()
        {
            using (var connection = CreateConnection("defaultdb"))
            {
                await connection.OpenAsync();
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT VERSION();";
                    command.CommandTimeout = 15;
                    return Convert.ToString(await command.ExecuteScalarAsync());
                }
            }
        }

        internal static MySqlConnection CreateConnection(string databaseName)
        {
            var password = Environment.GetEnvironmentVariable(
                "MUSICBOX_AIVEN_PASSWORD", EnvironmentVariableTarget.User);
            if (string.IsNullOrWhiteSpace(password))
                throw new InvalidOperationException(
                    "Chưa cấu hình biến người dùng MUSICBOX_AIVEN_PASSWORD trong Windows.");

            const string certificatePath = @"D:\MusicBoxSecrets\ca.pem";
            if (!File.Exists(certificatePath))
                throw new InvalidOperationException(
                    "Không tìm thấy chứng chỉ Aiven: " + certificatePath);

            var settings = new MySqlConnectionStringBuilder
            {
                Server = "musicbox-mysql-baitapnet.j.aivencloud.com",
                Port = 12628,
                UserID = "avnadmin",
                Password = password,
                Database = databaseName,
                SslMode = MySqlSslMode.VerifyFull,
                SslCa = certificatePath,
                ConnectionTimeout = 15
            };

            return new MySqlConnection(settings.ConnectionString);
        }
    }
}
