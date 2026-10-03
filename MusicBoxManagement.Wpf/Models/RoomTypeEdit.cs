namespace MusicBoxManagement.Wpf.Models
{
    // Only the five fields allowed by the plan; identity/code are not editable.
    public sealed class RoomTypeEdit
    {
        public string Name { get; set; }
        public int Capacity { get; set; }
        public long PricePerHour { get; set; }
        public string Amenities { get; set; }
        public string Description { get; set; }
    }
}
