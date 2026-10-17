using System.Collections.Generic;
using System.Linq;

namespace MusicBoxManagement.Wpf.Models
{
    public sealed class RolePermissionMatrix
    {
        public string Role { get; }
        public string Revision { get; }
        public IReadOnlyList<string> PermissionCodes { get; }

        internal RolePermissionMatrix(string role, string revision, IEnumerable<string> codes)
        { Role = role; Revision = revision; PermissionCodes = codes.ToList().AsReadOnly(); }
    }
}
