namespace MusicBoxManagement.Wpf.Models
{
    public sealed class ServiceItem
    {
        public int ServiceId { get; set; }
        public string Name { get; set; }
        public string Category { get; set; }
        public long Price { get; set; }
        public string Description { get; set; }
        public bool IsActive { get; set; }
        public string ActiveLabel => IsActive ? "Đang bán" : "Ngừng bán";
    }
    public sealed class ServiceEdit
    {
        public string Name { get; set; }
        public string Category { get; set; }
        public long Price { get; set; }
        public string Description { get; set; }
        public bool IsActive { get; set; }
    }
}
