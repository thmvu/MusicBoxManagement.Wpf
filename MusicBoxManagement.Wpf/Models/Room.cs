namespace MusicBoxManagement.Wpf.Models
{
    public sealed class Room
    {
        public int RoomId { get; set; }
        public string RoomCode { get; set; }
        public int RoomTypeId { get; set; }
        public string RoomTypeName { get; set; }
        public string Name { get; set; }
        public string ImageUrl { get; set; }
        public string Description { get; set; }
        public bool IsActive { get; set; }
        public string InactiveReason { get; set; }
        public string CreatedAt { get; set; }
        public string ActiveLabel => IsActive ? "Đang mở" : "Đã khóa";
    }

    public sealed class RoomCreate
    {
        public string RoomCode { get; set; }
        public int RoomTypeId { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public string ImageFilePath { get; set; }
    }
}
