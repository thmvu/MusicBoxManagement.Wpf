using System;
using System.Linq;

namespace MusicBoxManagement.Wpf.Services
{
    public static class PhoneNumberNormalizer
    {
        public const string ValidationMessage = "SĐT phải có 10 chữ số bắt đầu bằng 0; có thể nhập tiền tố +84 hoặc 84.";

        public static bool TryNormalize(string input, out string normalized)
        {
            var phone = new string((input ?? "").Where(c => !char.IsWhiteSpace(c) && c != '.' && c != '-').ToArray());
            if (phone.StartsWith("+84", StringComparison.Ordinal)) phone = "0" + phone.Substring(3);
            else if (phone.StartsWith("84", StringComparison.Ordinal)) phone = "0" + phone.Substring(2);
            var valid = phone.Length == 10 && phone[0] == '0' && phone.All(c => c >= '0' && c <= '9');
            normalized = valid ? phone : null;
            return valid;
        }

        public static string Normalize(string input)
        {
            if (!TryNormalize(input, out var phone)) throw new ArgumentException(ValidationMessage);
            return phone;
        }
    }
}
