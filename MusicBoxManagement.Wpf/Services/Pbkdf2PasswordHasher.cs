using System;
using System.Globalization;
using System.Security.Cryptography;
using Microsoft.AspNet.Identity;

namespace MusicBoxManagement.Wpf.Services
{
    public sealed class Pbkdf2PasswordHasher : IPasswordHasher
    {
        private const int Iterations = 600000;

        public string HashPassword(string password)
        {
            if (password == null) throw new ArgumentNullException(nameof(password));
            var salt = new byte[16];
            using (var random = RandomNumberGenerator.Create()) random.GetBytes(salt);
            using (var derive = new Rfc2898DeriveBytes(password, salt, Iterations, HashAlgorithmName.SHA256))
                return "PBKDF2-SHA256$1$" + Iterations.ToString(CultureInfo.InvariantCulture) + "$" +
                    Convert.ToBase64String(salt) + "$" + Convert.ToBase64String(derive.GetBytes(32));
        }

        public PasswordVerificationResult VerifyHashedPassword(string hashedPassword, string providedPassword)
        {
            if (hashedPassword == null || providedPassword == null) return PasswordVerificationResult.Failed;
            var parts = hashedPassword.Split('$');
            if (parts.Length != 5 || parts[0] != "PBKDF2-SHA256" || parts[1] != "1" ||
                !int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out var rounds) ||
                rounds < Iterations || rounds > 2000000) return PasswordVerificationResult.Failed;
            try
            {
                var salt = Convert.FromBase64String(parts[3]);
                var expected = Convert.FromBase64String(parts[4]);
                if (salt.Length != 16 || expected.Length != 32) return PasswordVerificationResult.Failed;
                using (var derive = new Rfc2898DeriveBytes(providedPassword, salt, rounds, HashAlgorithmName.SHA256))
                {
                    var actual = derive.GetBytes(32);
                    var difference = 0;
                    for (var i = 0; i < actual.Length; i++) difference |= actual[i] ^ expected[i];
                    return difference == 0 ? PasswordVerificationResult.Success : PasswordVerificationResult.Failed;
                }
            }
            catch (FormatException) { return PasswordVerificationResult.Failed; }
        }
    }
}
