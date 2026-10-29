// Shared contracts for the existing SQLite and MySQL providers. No database work here.
namespace MusicBoxManagement.Wpf.Services
{
    public interface IAuthenticationService
    {
        void Logout(MusicBoxManagement.Wpf.Models.LoginSession @session);
        System.Boolean NeedsSetup();
        System.Threading.Tasks.Task<MusicBoxManagement.Wpf.Models.LoginSession> SetupAdminAsync(System.String @userName, System.String @fullName, System.String @password);
        System.Threading.Tasks.Task<MusicBoxManagement.Wpf.Models.LoginSession> LoginAsync(System.String @userName, System.String @password);
    }
    public interface IPermissionService
    {
        MusicBoxManagement.Wpf.Services.StaffAccess GetStaffAccess(MusicBoxManagement.Wpf.Models.LoginSession @session);
        System.Boolean HasPermission(MusicBoxManagement.Wpf.Models.LoginSession @session, System.String @code);
        void Demand(MusicBoxManagement.Wpf.Models.LoginSession @session, System.String @code);
    }
    public interface IRoomTypeService
    {
        System.Collections.Generic.List<MusicBoxManagement.Wpf.Models.RoomType> List();
        MusicBoxManagement.Wpf.Models.RoomType GetForEdit(MusicBoxManagement.Wpf.Models.LoginSession @session, System.Int32 @roomTypeId);
        void Update(MusicBoxManagement.Wpf.Models.LoginSession @session, MusicBoxManagement.Wpf.Models.RoomType @original, MusicBoxManagement.Wpf.Models.RoomTypeEdit @changes);
    }
    public interface IRoomService
    {
        System.Collections.Generic.List<MusicBoxManagement.Wpf.Models.Room> ListForManagement(MusicBoxManagement.Wpf.Models.LoginSession @session);
        System.Int32 Create(MusicBoxManagement.Wpf.Models.LoginSession @session, MusicBoxManagement.Wpf.Models.RoomCreate @input);
        MusicBoxManagement.Wpf.Models.Room GetForEdit(MusicBoxManagement.Wpf.Models.LoginSession @session, System.Int32 @roomId);
        void Update(MusicBoxManagement.Wpf.Models.LoginSession @session, MusicBoxManagement.Wpf.Models.Room @original, MusicBoxManagement.Wpf.Models.RoomEdit @input);
        System.String GetImagePath(System.String @imageUrl);
    }
    public interface IServiceCatalogService
    {
        System.Collections.Generic.List<MusicBoxManagement.Wpf.Models.ServiceItem> ListForManagement(MusicBoxManagement.Wpf.Models.LoginSession @session);
        System.Int32 Save(MusicBoxManagement.Wpf.Models.LoginSession @session, MusicBoxManagement.Wpf.Models.ServiceItem @original, MusicBoxManagement.Wpf.Models.ServiceEdit @input);
    }
    public interface ICustomerService
    {
        ICustomerHistoryService ForHistory();
        MusicBoxManagement.Wpf.Models.CustomerList Search(MusicBoxManagement.Wpf.Models.LoginSession @session, System.String @nameQuery = null, System.String @phoneQuery = null);
        MusicBoxManagement.Wpf.Models.Customer Save(MusicBoxManagement.Wpf.Models.LoginSession @session, MusicBoxManagement.Wpf.Models.Customer @original, MusicBoxManagement.Wpf.Models.CustomerEdit @input);
    }
    public interface IGuestReservationService
    {
        IGuestSessionService ForSessions();
        IOrderService ForOrders();
        IBillingService ForBilling();
        MusicBoxManagement.Wpf.Models.GuestReservationLookup Lookup(System.String @phoneNumber);
        void Cancel(System.String @phoneNumber, System.Int32 @reservationId);
    }
    public interface IGuestSessionService
    {
        MusicBoxManagement.Wpf.Models.GuestSession Lookup(System.String @phoneNumber);
        MusicBoxManagement.Wpf.Models.SessionExtensionCheck PreviewExtension(System.String @phoneNumber, System.Int32 @sessionId, System.Int32 @minutes);
        MusicBoxManagement.Wpf.Models.GuestSession Extend(System.String @phoneNumber, System.Int32 @sessionId, System.DateTimeOffset @observedEnd, System.Int32 @minutes);
    }
    public interface IOrderService
    {
        MusicBoxManagement.Wpf.Models.OrderWorkspace ReadWorkspaceStaff(MusicBoxManagement.Wpf.Models.LoginSession @actor);
        MusicBoxManagement.Wpf.Models.ServiceOrder CreateGuest(System.String @phoneNumber, System.Int32 @sessionId, System.Collections.Generic.IEnumerable<MusicBoxManagement.Wpf.Models.OrderLineRequest> @items);
        MusicBoxManagement.Wpf.Models.ServiceOrder CreateStaff(MusicBoxManagement.Wpf.Models.LoginSession @actor, System.Int32 @sessionId, System.Collections.Generic.IEnumerable<MusicBoxManagement.Wpf.Models.OrderLineRequest> @items);
        MusicBoxManagement.Wpf.Models.OrderMenu ReadMenuGuest(System.String @phoneNumber, System.Int32 @sessionId);
        MusicBoxManagement.Wpf.Models.OrderMenu ReadMenuStaff(MusicBoxManagement.Wpf.Models.LoginSession @actor, System.Int32 @sessionId);
        MusicBoxManagement.Wpf.Models.OrderPreview PreviewGuest(System.String @phoneNumber, System.Int32 @sessionId, System.Collections.Generic.IEnumerable<MusicBoxManagement.Wpf.Models.OrderLineRequest> @items);
        MusicBoxManagement.Wpf.Models.OrderPreview PreviewStaff(MusicBoxManagement.Wpf.Models.LoginSession @actor, System.Int32 @sessionId, System.Collections.Generic.IEnumerable<MusicBoxManagement.Wpf.Models.OrderLineRequest> @items);
        MusicBoxManagement.Wpf.Models.ServiceOrder ConfirmStaff(MusicBoxManagement.Wpf.Models.LoginSession @actor, System.Int32 @orderId);
        MusicBoxManagement.Wpf.Models.ServiceOrder CancelStaff(MusicBoxManagement.Wpf.Models.LoginSession @actor, System.Int32 @orderId);
        MusicBoxManagement.Wpf.Models.ServiceOrder CancelGuest(System.String @phoneNumber, System.Int32 @orderId);
        System.Collections.Generic.List<MusicBoxManagement.Wpf.Models.ServiceOrder> ListGuest(System.String @phoneNumber, System.Int32 @sessionId);
        System.Collections.Generic.List<MusicBoxManagement.Wpf.Models.ServiceOrder> ListStaff(MusicBoxManagement.Wpf.Models.LoginSession @actor, System.Int32 @sessionId);
    }
    public interface IBillingService
    {
        MusicBoxManagement.Wpf.Models.SessionBill ReadGuest(System.String @phoneNumber, System.Int32 @sessionId);
        MusicBoxManagement.Wpf.Models.SessionBill ReadStaff(MusicBoxManagement.Wpf.Models.LoginSession @actor, System.Int32 @sessionId);
    }
    public interface IRoomSessionService
    {
        MusicBoxManagement.Wpf.Models.RoomSession CheckIn(MusicBoxManagement.Wpf.Models.LoginSession @actor, System.Int32 @reservationId);
        MusicBoxManagement.Wpf.Models.SessionExtensionCheck PreviewExtensionStaff(MusicBoxManagement.Wpf.Models.LoginSession @actor, System.Int32 @sessionId, System.Int32 @minutes);
        MusicBoxManagement.Wpf.Models.RoomSession ExtendStaff(MusicBoxManagement.Wpf.Models.LoginSession @actor, System.Int32 @sessionId, System.DateTimeOffset @observedEnd, System.Int32 @minutes);
        System.Collections.Generic.List<MusicBoxManagement.Wpf.Models.PublicRoom> ListWalkInRooms(MusicBoxManagement.Wpf.Models.LoginSession @actor);
        MusicBoxManagement.Wpf.Models.WalkInPreview PreviewWalkIn(MusicBoxManagement.Wpf.Models.LoginSession @actor, MusicBoxManagement.Wpf.Models.WalkInRequest @request);
        MusicBoxManagement.Wpf.Models.WalkInResult ReadWalkInReceipt(MusicBoxManagement.Wpf.Models.LoginSession @actor, System.Int32 @sessionId);
        MusicBoxManagement.Wpf.Models.WalkInResult CreateWalkIn(MusicBoxManagement.Wpf.Models.LoginSession @actor, MusicBoxManagement.Wpf.Models.WalkInRequest @request);
        MusicBoxManagement.Wpf.Models.WalkInResult ReadWalkIn(MusicBoxManagement.Wpf.Models.LoginSession @actor, System.Int32 @sessionId);
    }
    public interface IStaffSessionService
    {
        IBillingService ForBilling();
        MusicBoxManagement.Wpf.Models.StaffSessionSearch Search(MusicBoxManagement.Wpf.Models.LoginSession @actor, System.String @phoneNumber = null, System.String @status = "Active");
        MusicBoxManagement.Wpf.Models.SessionExtensionCheck Preview(MusicBoxManagement.Wpf.Models.LoginSession @actor, System.Int32 @sessionId, System.Int32 @minutes);
        MusicBoxManagement.Wpf.Models.RoomSession Extend(MusicBoxManagement.Wpf.Models.LoginSession @actor, System.Int32 @sessionId, System.DateTimeOffset @observedEnd, System.Int32 @minutes);
    }
    public interface ICheckoutService
    {
        MusicBoxManagement.Wpf.Models.CheckoutPreview PreviewStaff(MusicBoxManagement.Wpf.Models.LoginSession @actor, System.Int32 @sessionId);
        System.Collections.Generic.IReadOnlyList<MusicBoxManagement.Wpf.Models.CheckoutSessionChoice> ListActiveStaff(MusicBoxManagement.Wpf.Models.LoginSession @actor);
        MusicBoxManagement.Wpf.Models.Invoice ConfirmStaff(MusicBoxManagement.Wpf.Models.LoginSession @actor, System.Int32 @sessionId, System.String @paymentMethod);
    }
    public interface IDashboardService
    {
        MusicBoxManagement.Wpf.Models.DashboardSnapshot Read(MusicBoxManagement.Wpf.Models.LoginSession @actor);
        System.Decimal ReadRevenueToday(MusicBoxManagement.Wpf.Models.LoginSession @actor);
    }
    public interface IAccountManagementService
    {
        System.Collections.Generic.IReadOnlyList<MusicBoxManagement.Wpf.Models.UserAccount> List(MusicBoxManagement.Wpf.Models.LoginSession @actor);
        System.Threading.Tasks.Task<MusicBoxManagement.Wpf.Models.UserAccount> CreateAsync(MusicBoxManagement.Wpf.Models.LoginSession @actor, MusicBoxManagement.Wpf.Models.AccountEdit @input, System.String @password);
        System.Threading.Tasks.Task<MusicBoxManagement.Wpf.Models.UserAccount> UpdateAsync(MusicBoxManagement.Wpf.Models.LoginSession @actor, MusicBoxManagement.Wpf.Models.UserAccount @original, MusicBoxManagement.Wpf.Models.AccountEdit @input);
        System.Threading.Tasks.Task<MusicBoxManagement.Wpf.Models.UserAccount> ResetPasswordAsync(MusicBoxManagement.Wpf.Models.LoginSession @actor, MusicBoxManagement.Wpf.Models.UserAccount @original, System.String @password);
    }
    public interface IPermissionManagementService
    {
        MusicBoxManagement.Wpf.Models.RolePermissionMatrix Read(MusicBoxManagement.Wpf.Models.LoginSession @actor, System.String @role);
        MusicBoxManagement.Wpf.Models.RolePermissionMatrix Update(MusicBoxManagement.Wpf.Models.LoginSession @actor, MusicBoxManagement.Wpf.Models.RolePermissionMatrix @original, System.Collections.Generic.IEnumerable<System.String> @selectedCodes);
    }
    public interface IAuditLogService
    {
        MusicBoxManagement.Wpf.Models.AuditLogPage Search(MusicBoxManagement.Wpf.Models.LoginSession @actor, System.Nullable<System.DateTime> @fromDate = null, System.Nullable<System.DateTime> @toDate = null, System.String @actorType = null, System.String @action = null, System.String @entityName = null, System.Nullable<System.Int32> @beforeId = null, System.Int32 @pageSize = 100);
        MusicBoxManagement.Wpf.Models.AuditLogEntry Read(MusicBoxManagement.Wpf.Models.LoginSession @actor, System.Int32 @auditLogId);
    }
    public interface IReportService
    {
        MusicBoxManagement.Wpf.Models.RevenueReport ReadRevenue(MusicBoxManagement.Wpf.Models.LoginSession @actor, System.DateTime @fromDate, System.DateTime @toDate, System.String @roomTypeCode = null);
        MusicBoxManagement.Wpf.Models.ReportExcelFile ExportRevenue(MusicBoxManagement.Wpf.Models.LoginSession @actor, System.DateTime @fromDate, System.DateTime @toDate, System.String @roomTypeCode = null, System.String @mode = "Day");
    }
    public interface IRoomUsageReportService
    {
        MusicBoxManagement.Wpf.Models.RoomUsageReport Read(MusicBoxManagement.Wpf.Models.LoginSession @actor, System.DateTime @fromDate, System.DateTime @toDate, System.Nullable<System.Int32> @roomId = null);
        MusicBoxManagement.Wpf.Models.ReportExcelFile Export(MusicBoxManagement.Wpf.Models.LoginSession @actor, System.DateTime @fromDate, System.DateTime @toDate, System.Nullable<System.Int32> @roomId = null);
    }
    public interface IBookingStatusReportService
    {
        MusicBoxManagement.Wpf.Models.BookingStatusReport Read(MusicBoxManagement.Wpf.Models.LoginSession @actor, System.DateTime @fromDate, System.DateTime @toDate);
        MusicBoxManagement.Wpf.Models.ReportExcelFile Export(MusicBoxManagement.Wpf.Models.LoginSession @actor, System.DateTime @fromDate, System.DateTime @toDate);
    }
    public interface IServiceSalesReportService
    {
        MusicBoxManagement.Wpf.Models.ServiceSalesReport Read(MusicBoxManagement.Wpf.Models.LoginSession @actor, System.DateTime @fromDate, System.DateTime @toDate, System.Nullable<System.Int32> @serviceId = null);
        MusicBoxManagement.Wpf.Models.ReportExcelFile Export(MusicBoxManagement.Wpf.Models.LoginSession @actor, System.DateTime @fromDate, System.DateTime @toDate, System.Nullable<System.Int32> @serviceId = null, System.Boolean @topItems = false);
    }
    public interface ICustomerHistoryService
    {
        MusicBoxManagement.Wpf.Models.CustomerHistory Read(MusicBoxManagement.Wpf.Models.LoginSession @actor, System.Int32 @customerId);
    }
    public interface ICalendarService
    {
        System.Collections.Generic.IReadOnlyList<MusicBoxManagement.Wpf.Models.RoomSchedule> Read(MusicBoxManagement.Wpf.Models.LoginSession @session, MusicBoxManagement.Wpf.Models.CalendarRange @range, System.Nullable<System.Int32> @roomId = null);
    }
    public interface IStaffReservationService
    {
        IRoomSessionService ForWalkIn(MusicBoxManagement.Wpf.Models.LoginSession @session);
        IStaffSessionService ForSessions(MusicBoxManagement.Wpf.Models.LoginSession @session);
        IOrderService ForOrders(MusicBoxManagement.Wpf.Models.LoginSession @session);
        ICheckoutService ForCheckout(MusicBoxManagement.Wpf.Models.LoginSession @session);
        IInvoiceService ForInvoices(MusicBoxManagement.Wpf.Models.LoginSession @session);
        IDashboardService ForDashboard(MusicBoxManagement.Wpf.Models.LoginSession @session);
        IAuditLogService ForAudit(MusicBoxManagement.Wpf.Models.LoginSession @session);
        IReportService ForReports(MusicBoxManagement.Wpf.Models.LoginSession @session);
        IRoomUsageReportService ForRoomReports(MusicBoxManagement.Wpf.Models.LoginSession @session);
        IBookingStatusReportService ForBookingReports(MusicBoxManagement.Wpf.Models.LoginSession @session);
        IPermissionManagementService ForPermissions(MusicBoxManagement.Wpf.Models.LoginSession @session);
        IServiceSalesReportService ForServiceReports(MusicBoxManagement.Wpf.Models.LoginSession @session);
        IAccountManagementService ForAccounts(MusicBoxManagement.Wpf.Models.LoginSession @session);
        MusicBoxManagement.Wpf.Models.StaffReservationSearch Search(MusicBoxManagement.Wpf.Models.LoginSession @session, System.Nullable<System.DateTime> @fromDate = null, System.Nullable<System.DateTime> @toDate = null, System.String @phoneNumber = null, System.String @status = null);
        void Cancel(MusicBoxManagement.Wpf.Models.LoginSession @session, System.Int32 @reservationId, System.String @reason);
        MusicBoxManagement.Wpf.Models.RoomSession CheckIn(MusicBoxManagement.Wpf.Models.LoginSession @session, System.Int32 @reservationId);
        ICalendarService ForCalendar(MusicBoxManagement.Wpf.Models.LoginSession @session);
        IBookingService ForBooking(MusicBoxManagement.Wpf.Models.LoginSession @session);
    }
}
