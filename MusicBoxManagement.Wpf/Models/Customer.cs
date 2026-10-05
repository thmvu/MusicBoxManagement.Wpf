using System.Collections.Generic;

namespace MusicBoxManagement.Wpf.Models
{
    public sealed class Customer
    {
        public int CustomerId { get; set; }
        public string FullName { get; set; }
        public string PhoneNumber { get; set; }
    }
    public sealed class CustomerEdit
    {
        public string FullName { get; set; }
        public string PhoneNumber { get; set; }
    }
    public sealed class CustomerList
    {
        public List<Customer> Items { get; internal set; }
        public bool CanCreate { get; internal set; }
        public bool CanEdit { get; internal set; }
    }
}
