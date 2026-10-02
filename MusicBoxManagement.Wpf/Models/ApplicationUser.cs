using Microsoft.AspNet.Identity.EntityFramework;

namespace MusicBoxManagement.Wpf.Models
{
    public sealed class ApplicationUser : IdentityUser
    {
        public string FullName { get; set; }
        public bool IsActive { get; set; } = true;
    }
}
