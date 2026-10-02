namespace MusicBoxManagement.Wpf.Models
{
    // Only held in memory. Permissions and account status are read from SQLite.
    public sealed class LoginSession
    {
        public string UserId { get; }
        public string SecurityStamp { get; }
        internal volatile bool IsSignedOut;

        internal LoginSession(string userId, string securityStamp)
        {
            UserId = userId;
            SecurityStamp = securityStamp;
        }
    }
}
