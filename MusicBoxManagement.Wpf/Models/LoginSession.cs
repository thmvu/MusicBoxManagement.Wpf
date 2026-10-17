namespace MusicBoxManagement.Wpf.Models
{
    // Only held in memory. Permissions and account status are read from the current database.
    public sealed class LoginSession
    {
        public string UserId { get; }
        public string SecurityStamp { get; }
        internal volatile bool IsSignedOut;
        internal string DatabaseScope { get; }

        internal LoginSession(string userId, string securityStamp, string databaseScope = null)
        {
            DatabaseScope = databaseScope;
            UserId = userId;
            SecurityStamp = securityStamp;
        }
    }
}
