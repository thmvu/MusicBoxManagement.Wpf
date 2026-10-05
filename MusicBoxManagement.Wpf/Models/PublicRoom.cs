namespace MusicBoxManagement.Wpf.Models
{
    public sealed class PublicRoom
    {
        public int RoomId { get; internal set; }
        public string RoomCode { get; internal set; }
        public string Name { get; internal set; }
        public string TypeName { get; internal set; }
        public int Capacity { get; internal set; }
        public long PricePerHour { get; internal set; }
        public string Amenities { get; internal set; }
        public string Description { get; internal set; }
        public string ImageUrl { get; internal set; }
    }
}
