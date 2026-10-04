using System;
using System.IO;
using SkiaSharp;

namespace MusicBoxManagement.Wpf.Services
{
    internal static class RoomImages
    {
        // Decode the actual bytes, including WebP, rather than trusting a file extension.
        // Store one normalized PNG so WPF can display every accepted input format.
        internal static byte[] ReadPng(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath)) throw new ArgumentException("Cần chọn một ảnh phòng.");
            byte[] bytes;
            using (var stream = File.OpenRead(filePath))
            {
                if (stream.Length == 0 || stream.Length > 5 * 1024 * 1024)
                    throw new ArgumentException("Ảnh phải có dữ liệu và không quá 5 MB.");
                bytes = new byte[(int)stream.Length];
                var offset = 0;
                while (offset < bytes.Length)
                {
                    var read = stream.Read(bytes, offset, bytes.Length - offset);
                    if (read == 0) throw new ArgumentException("Không đọc được toàn bộ ảnh.");
                    offset += read;
                }
            }
            using (var data = SKData.CreateCopy(bytes))
            using (var codec = SKCodec.Create(data))
            {
                if (codec == null || (codec.EncodedFormat != SKEncodedImageFormat.Jpeg &&
                    codec.EncodedFormat != SKEncodedImageFormat.Png && codec.EncodedFormat != SKEncodedImageFormat.Webp))
                    throw new ArgumentException("Chỉ nhận ảnh JPEG, PNG hoặc WebP hợp lệ.");
                var info = new SKImageInfo(codec.Info.Width, codec.Info.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
                using (var bitmap = new SKBitmap())
                {
                    if (!bitmap.TryAllocPixels(info) || codec.GetPixels(info, bitmap.GetPixels()) != SKCodecResult.Success)
                        throw new ArgumentException("Ảnh bị hỏng hoặc không giải mã được đầy đủ.");
                    using (var image = SKImage.FromBitmap(bitmap))
                    using (var png = image.Encode(SKEncodedImageFormat.Png, 100))
                        return png.ToArray();
                }
            }
        }
    }
}
