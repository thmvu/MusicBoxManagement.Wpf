namespace MusicBoxManagement.Wpf.Models
{
    public sealed class UserAccount
    {
        public string UserId { get; internal set; }
        public string UserName { get; internal set; }
        public string FullName { get; internal set; }
        public string Role { get; internal set; }
        public bool IsActive { get; internal set; }
        public string Revision { get; internal set; }
    }
    public sealed class AccountEdit
    {
        public string UserName { get; set; }
        public string FullName { get; set; }
        public string Role { get; set; }
        public bool IsActive { get; set; } = true;
    }
}
