using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace MusicBoxManagement.Wpf.Services
{
    public static class PermissionCatalog
    {
        public static IReadOnlyDictionary<string, string> All { get; } =
            new ReadOnlyDictionary<string, string>(new Dictionary<string, string>
            {
                { "Dashboard.View", "Xem bảng điều hành" },
                { "Calendar.View", "Xem lịch ngày/tuần" },
                { "Reservation.View", "Xem đặt phòng" },
                { "Reservation.Create", "Tạo đặt phòng" },
                { "Reservation.Cancel", "Hủy đặt phòng" },
                { "Session.View", "Xem phiên sử dụng" },
                { "Session.CheckIn", "Nhận phòng đã đặt" },
                { "Session.WalkIn", "Nhận khách trực tiếp" },
                { "Session.Extend", "Gia hạn phiên" },
                { "Session.CheckOut", "Trả phòng và thanh toán" },
                { "Order.View", "Xem gọi món" },
                { "Order.Create", "Tạo gọi món" },
                { "Order.Confirm", "Xác nhận món" },
                { "Order.Cancel", "Hủy món chờ" },
                { "Invoice.View", "Xem hóa đơn" },
                { "Invoice.Print", "In hóa đơn" },
                { "Customer.View", "Xem khách hàng" },
                { "Customer.Create", "Tạo khách hàng" },
                { "Customer.Edit", "Sửa khách hàng" },
                { "Room.Manage", "Quản lý phòng" },
                { "RoomType.Edit", "Sửa loại phòng" },
                { "Service.Manage", "Quản lý dịch vụ" },
                { "Report.View", "Xem báo cáo" },
                { "Report.Export", "Xuất báo cáo Excel" },
                { "User.Manage", "Quản lý tài khoản" },
                { "Permission.Manage", "Chỉnh ma trận quyền" },
                { "Audit.View", "Xem nhật ký" }
            });

        public static bool IsAdministrative(string code) =>
            code == "User.Manage" || code == "Permission.Manage" || code == "Audit.View";

        public static IEnumerable<string> Defaults(string role) => All.Keys.Where(code =>
            role == "Admin" || (!IsAdministrative(code) &&
            (role == "Manager" || (role == "Staff" && !code.StartsWith("Report.") &&
                code != "Room.Manage" && code != "RoomType.Edit" && code != "Service.Manage"))));
    }
}
