namespace MusicBoxManagement.Wpf.Models
{
    public sealed class RoomType
    {
        public int RoomTypeId { get; set; }
        public string Code { get; set; }
        public string Name { get; set; }
        public int Capacity { get; set; }
        public long PricePerHour { get; set; }
        public string Amenities { get; set; }
        public string Description { get; set; }
    }
}
