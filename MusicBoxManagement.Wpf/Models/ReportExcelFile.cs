using System;
using System.IO;

namespace MusicBoxManagement.Wpf.Models
{
    public sealed class ReportExcelFile
    {
        public string FileName { get; }
        private readonly byte[] content;
        internal ReportExcelFile(string fileName, byte[] content) { FileName = fileName; this.content = content; }
        public byte[] GetBytes() => (byte[])content.Clone();

        // The caller obtains a freshly authorized export after the Save dialog.
        // Same-directory replacement preserves an existing file if generation/write fails.
        public void SaveTo(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !string.Equals(Path.GetExtension(path), ".xlsx", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Cần chọn file có phần mở rộng .xlsx.");
            var target = Path.GetFullPath(path);
            var temporary = Path.Combine(Path.GetDirectoryName(target), ".musicbox-export-" + Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                    stream.Write(content, 0, content.Length);
                if (File.Exists(target)) File.Replace(temporary, target, null);
                else File.Move(temporary, target);
            }
            catch (UnauthorizedAccessException error)
            { throw new IOException("Không có quyền ghi file Excel vào vị trí đã chọn.", error); }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
    }
}
