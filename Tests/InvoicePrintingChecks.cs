using System;
using System.Data.SQLite;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Xml;
using MusicBoxManagement.Wpf.Data;
using MusicBoxManagement.Wpf.Models;
using MusicBoxManagement.Wpf.Printing;
using MusicBoxManagement.Wpf.Services;
using MusicBoxManagement.Wpf.ViewModels;
using MusicBoxManagement.Wpf.Views;

public static class MusicBoxInvoicePrintingChecks
{
    private sealed class Clock : IClock
    {
        public DateTimeOffset Value; public bool Block;
        public readonly ManualResetEventSlim Entered = new ManualResetEventSlim(false), Release = new ManualResetEventSlim(false);
        public DateTimeOffset UtcNow { get { if (Block) { Entered.Set(); if (!Release.Wait(10000)) throw new Exception("Print gate timeout."); } return Value; } }
    }
    private sealed class Printer : IInvoicePrinter
    {
        public int Selections, Sends; public bool Accepted = true, FailSend; public Action OnSelect;
        public DocumentPaginator Output; public string JobName;
        public Size Area = new Size(720, 1040);
        public bool Select() { Selections++; if (OnSelect != null) OnSelect(); return Accepted; }
        public Size PrintableArea { get { return Area; } }
        public void Send(DocumentPaginator document, string jobName)
        { Sends++; if (FailSend) throw new IOException("Fake queue failure"); Output = document; JobName = jobName; }
    }
    private static object Sql(SqliteDatabase db, string sql)
    { using (var connection = db.OpenConnection()) using (var command = connection.CreateCommand()) { command.CommandText = sql; return command.ExecuteScalar(); } }
    private static void Assert(bool value, string message) { if (!value) throw new Exception(message); }
    private static void Pump(Func<bool> ready)
    {
        var watch = Stopwatch.StartNew();
        while (!ready())
        {
            if (watch.Elapsed.TotalSeconds > 30) throw new Exception("Printing UI timed out.");
            var frame = new DispatcherFrame();
            Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => frame.Continue = false));
            Dispatcher.PushFrame(frame); Thread.Sleep(10);
        }
    }
    private static void Await(Task task) { Pump(() => task.IsCompleted); task.GetAwaiter().GetResult(); }
    private static T Await<T>(Task<T> task) { Await((Task)task); return task.GetAwaiter().GetResult(); }
    private static string Text(FlowDocument document) { return new TextRange(document.ContentStart, document.ContentEnd).Text; }
    private static void Save(Visual visual, int width, int height, string output)
    {
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32); bitmap.Render(visual);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using (var stream = File.Create(output)) encoder.Save(stream);
    }
    public static void Run(string appXaml, string outputDirectory)
    {
        Directory.CreateDirectory(outputDirectory);
        var directory = Path.Combine(Path.GetTempPath(), "MusicBoxPrintData_" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        var xml = new XmlDocument(); xml.Load(appXaml);
        app.Resources = (ResourceDictionary)XamlReader.Parse("<ResourceDictionary xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'>"
            + xml.DocumentElement.FirstChild.InnerXml + "</ResourceDictionary>");
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
        Window open = null; var clock = new Clock { Value = new DateTimeOffset(2026, 10, 13, 13, 0, 12, TimeSpan.FromHours(7)).AddTicks(1234) };
        try
        {
            var db = new SqliteDatabase(Path.Combine(directory, "print.db")); var auth = new AuthenticationService(db); const string password = "Print-Test-2026!";
            var admin = auth.SetupAdminAsync("admin", "Thu ngân gốc", password).GetAwaiter().GetResult();
            Sql(db, @"INSERT INTO Rooms(RoomCode,Name,RoomTypeId,ImageUrl,IsActive,CreatedAt) VALUES('P1','Phòng',1,'test.png',1,'test');
INSERT INTO Services(Name,Category,Price,IsActive) VALUES('Trà đào tên gốc','Đồ uống',25000,1);
INSERT INTO AspNetUsers(Id,UserName,NormalizedUserName,PasswordHash,SecurityStamp,FullName,IsActive) SELECT 'printer','printer','PRINTER',PasswordHash,'print','Nhân viên in',1 FROM AspNetUsers LIMIT 1;
INSERT INTO AspNetUserRoles VALUES('printer','Staff');DELETE FROM RolePermission WHERE RoleId='Staff';
INSERT INTO RolePermission SELECT 'Staff',PermissionId FROM Permission WHERE Code IN ('Invoice.View','Invoice.Print');");
            var actor = auth.LoginAsync("printer", password).GetAwaiter().GetResult();
            var session = new RoomSessionService(db, clock).CreateWalkIn(admin, new WalkInRequest { RoomId = 1, FullName = "Khách", PhoneNumber = "0901111111" }).Session;
            new OrderService(db, clock).CreateStaff(admin, session.RoomSessionId, new[] { new OrderLineRequest { ServiceId = 1, Quantity = 2 } });
            clock.Value = clock.Value.AddSeconds(90).AddTicks(2456);
            var invoice = new CheckoutService(db, clock).ConfirmStaff(admin, session.RoomSessionId, "Cash");
            Sql(db, "UPDATE RoomTypes SET Name='Tên hiện tại',PricePerHour=999000;UPDATE Services SET Name='Tên món hiện tại',Price=99000,IsActive=0;UPDATE AspNetUsers SET FullName='Thu ngân đổi' WHERE Id='" + admin.UserId + "';");
            var audit = Convert.ToInt64(Sql(db, "SELECT COUNT(*) FROM AuditLog;"));
            var service = new InvoiceService(db, clock); var parent = new InvoicesViewModel(service, actor);
            Await(parent.SearchAsync()); Await(parent.SelectAsync(parent.Items.Single()));
            Assert(parent.CanPrint, "View+Print-only cannot print independently.");
            var vm = Await(parent.PreparePrintAsync());
            var content = Text(vm.Document);
            Assert(content.Contains(invoice.InvoiceNumber) && content.Contains("Music Box") && content.Contains("P1") && content.Contains("Standard (STANDARD)")
                && content.Contains("Trà đào tên gốc") && content.Contains("25.000") && content.Contains("50.000") && content.Contains("53.000")
                && content.Contains("Thu ngân gốc") && content.Contains("13:00:12 13/10/2026") && content.Contains("13:01:42 13/10/2026")
                && content.Contains("Tiền mặt") && !content.Contains("hiện tại") && !content.Contains("0901111111"), "Print content lost snapshots/totals/time or leaked Customer PII.");
            Assert(vm.Paginator.PageCount == 1, "Short A4 invoice unexpectedly spans pages.");
            open = new InvoicePrintWindow(vm, new Printer()); open.Show(); open.UpdateLayout();
            Save(open, (int)open.ActualWidth, (int)open.ActualHeight, Path.Combine(outputDirectory, "invoice-print-preview.png"));
            open.Width = open.MinWidth; open.Height = open.MinHeight; open.UpdateLayout();
            Save(open, (int)open.ActualWidth, (int)open.ActualHeight, Path.Combine(outputDirectory, "invoice-print-compact.png"));
            Assert(((InvoicePrintWindow)open).PrintButton.IsEnabled, "Preview print button inaccessible.");
            open.Close(); open = null;
            var printer = new Printer { Accepted = false }; Await(vm.PrintAsync(printer));
            Assert(printer.Selections == 1 && printer.Sends == 0 && vm.CanPrint && vm.Status.Contains("hủy"), "Cancelled native dialog sent a job or disabled reprint.");
            printer.Accepted = true; Await(vm.PrintAsync(printer)); Await(vm.PrintAsync(printer));
            Assert(printer.Sends == 2 && printer.Output.PageSize == printer.Area && printer.JobName.Contains(invoice.InvoiceNumber) && vm.Status.Contains("Đã gửi lệnh"), "Reprint or printer area incorrect.");
            Sql(db, "DELETE FROM RolePermission WHERE RoleId='Staff' AND PermissionId=(SELECT PermissionId FROM Permission WHERE Code='Invoice.Print');");
            var blocked = new Printer(); Await(vm.PrintAsync(blocked));
            Assert(blocked.Selections == 0 && blocked.Sends == 0 && vm.Document == null && !vm.CanPrint, "Revoked Print used cached DTO or showed dialog.");
            Assert(service.Read(actor, invoice.InvoiceId).Invoice.InvoiceId == invoice.InvoiceId, "Print loss also blocked View service.");
            Sql(db, "INSERT INTO RolePermission SELECT 'Staff',PermissionId FROM Permission WHERE Code='Invoice.Print';");
            vm = new InvoicePrintViewModel(service, actor, service.ReadForPrint(actor, invoice.InvoiceId));
            var duringDialog = new Printer { OnSelect = () => Sql(db, "DELETE FROM RolePermission WHERE RoleId='Staff' AND PermissionId=(SELECT PermissionId FROM Permission WHERE Code='Invoice.View');") };
            Await(vm.PrintAsync(duringDialog)); Assert(duringDialog.Selections == 1 && duringDialog.Sends == 0 && vm.Document == null, "View revoked while selecting printer still dispatched.");
            Sql(db, "INSERT INTO RolePermission SELECT 'Staff',PermissionId FROM Permission WHERE Code='Invoice.View';");
            vm = new InvoicePrintViewModel(service, actor, service.ReadForPrint(actor, invoice.InvoiceId));
            var stale = new Printer { OnSelect = () => Sql(db, "UPDATE RoomSessions SET ActualEndTime='" + invoice.PaidAt.AddTicks(1).ToUniversalTime().ToString("O", CultureInfo.InvariantCulture) + "' WHERE RoomSessionId=" + session.RoomSessionId + ";") };
            Await(vm.PrintAsync(stale)); Assert(stale.Sends == 0 && vm.Document == null, "Snapshot became inconsistent during dialog but was printed.");
            Sql(db, "UPDATE RoomSessions SET ActualEndTime='" + invoice.PaidAt.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture) + "' WHERE RoomSessionId=" + session.RoomSessionId + ";");
            vm = new InvoicePrintViewModel(service, actor, service.ReadForPrint(actor, invoice.InvoiceId));
            var busyPrinter = new Printer(); clock.Block = true;
            var waiting = vm.PrintAsync(busyPrinter); Pump(() => clock.Entered.IsSet); Await(vm.PrintAsync(busyPrinter));
            open = new InvoicePrintWindow(vm, busyPrinter); open.Show(); open.Close();
            Assert(open.IsVisible && busyPrinter.Selections == 0 && !vm.CanPrint, "Busy print duplicated or closed midway.");
            clock.Block = false; clock.Release.Set(); Await(waiting); Assert(busyPrinter.Sends == 1, "One busy action did not send exactly once."); open.Close(); open = null;
            var failure = new Printer { FailSend = true }; Await(vm.PrintAsync(failure));
            Assert(vm.Document == null && !vm.CanPrint && vm.Status.Contains("hàng đợi"), "Printer failure falsely reported success or encouraged blind retry.");
            vm = new InvoicePrintViewModel(service, actor, service.ReadForPrint(actor, invoice.InvoiceId));
            var small = new Printer { Area = new Size(200, 300) }; Await(vm.PrintAsync(small));
            Assert(small.Sends == 0 && vm.Status.Contains("Vùng in quá nhỏ"), "Unsupported tiny page silently clipped content.");
            Assert(Convert.ToInt64(Sql(db, "SELECT COUNT(*) FROM AuditLog;")) == audit && Convert.ToInt64(Sql(db, "SELECT COUNT(*) FROM Invoices;")) == 1
                && Convert.ToInt64(Sql(db, "PRAGMA user_version;")) == SqliteDatabase.CurrentSchemaVersion, "Printing wrote invoice/audit/schema.");
            // Long real orders exercise pagination and money beyond Int64 without touching invoice snapshots.
            clock.Value = new DateTimeOffset(2026, 10, 13, 15, 0, 0, TimeSpan.FromHours(7));
            Sql(db, "UPDATE RoomTypes SET PricePerHour=9223372036854775807;UPDATE Services SET IsActive=1,Price=9223372036854775807,Name='Món dài giữ nguyên tên trong bản in nhiều trang để kiểm tra tự xuống dòng';");
            session = new RoomSessionService(db, clock).CreateWalkIn(admin, new WalkInRequest { RoomId = 1, FullName = "Khách nhiều món", PhoneNumber = "0902222222" }).Session;
            var orders = new OrderService(db, clock);
            for (var i = 0; i < 70; i++) orders.CreateStaff(admin, session.RoomSessionId, new[] { new OrderLineRequest { ServiceId = 1, Quantity = 10 } });
            clock.Value = clock.Value.AddHours(2);
            var manyInvoice = new CheckoutService(db, clock).ConfirmStaff(admin, session.RoomSessionId, "BankTransfer");
            var details = service.ReadForPrint(actor, manyInvoice.InvoiceId);
            vm = new InvoicePrintViewModel(service, actor, details);
            Assert(vm.Paginator.PageCount >= 3 && details.Items.Count == 70 && Text(vm.Document).Contains(manyInvoice.TotalAmount.ToString("N0", CultureInfo.GetCultureInfo("vi-VN")))
                && Text(vm.Document).Contains("Chuyển khoản"), "Long invoice did not paginate or lost precise totals.");
            for (var i = 0; i < vm.Paginator.PageCount; i++)
            {
                var page = vm.Paginator.GetPage(i); Assert(page != DocumentPage.Missing && page.ContentBox.Width <= InvoiceDocument.A4.Width, "Missing/overflow print page.");
                Save(page.Visual, (int)Math.Ceiling(page.Size.Width), (int)Math.Ceiling(page.Size.Height), Path.Combine(outputDirectory, "invoice-print-long-" + (i + 1) + ".png"));
            }
            open = new InvoicePrintWindow(vm, new Printer()); open.Show(); open.UpdateLayout();
            Save(open, (int)open.ActualWidth, (int)open.ActualHeight, Path.Combine(outputDirectory, "invoice-print-long-window.png")); open.Close(); open = null;
            clock.Value = clock.Value.AddMinutes(5);
            var emptySession = new RoomSessionService(db, clock).CreateWalkIn(admin, new WalkInRequest { RoomId = 1, FullName = "Khách không món", PhoneNumber = "0903333333" }).Session;
            clock.Value = clock.Value.AddMinutes(2);
            var emptyInvoice = new CheckoutService(db, clock).ConfirmStaff(admin, emptySession.RoomSessionId, "Cash");
            var empty = new InvoicePrintViewModel(service, actor, service.ReadForPrint(actor, emptyInvoice.InvoiceId));
            Assert(Text(empty.Document).Contains("Không có món đã phục vụ.") && Text(empty.Document).Contains("Tiền dịch vụ: 0 đ") && empty.Paginator.PageCount == 1, "No-item invoice omitted zero service total or failed pagination.");
            // Exercise the actual viewer entry point; close preview without invoking a native printer.
            var form = new InvoicesWindow(new InvoicesViewModel(service, actor)); open = form; form.Show();
            var actualParent = (InvoicesViewModel)form.DataContext; Pump(() => !actualParent.IsBusy && actualParent.Items.Count == 3);
            ((DataGrid)form.FindName("InvoicesTable")).SelectedItem = actualParent.Items.Single(i => i.InvoiceId == invoice.InvoiceId);
            Pump(() => !actualParent.IsBusy && actualParent.CanPrint);
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) }; bool reached = false;
            timer.Tick += (sender, args) => { var preview = app.Windows.OfType<InvoicePrintWindow>().FirstOrDefault(); if (preview != null) { reached = true; timer.Stop(); preview.Close(); } };
            timer.Start(); ((Button)form.FindName("PrintButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump(() => reached && !actualParent.IsBusy); timer.Stop();
            Assert(actualParent.HasDetails, "Closing preview cleared settled invoice view.");
            Sql(db, "DELETE FROM RolePermission WHERE RoleId='Staff' AND PermissionId=(SELECT PermissionId FROM Permission WHERE Code='Invoice.Print');");
            Assert(Await(actualParent.PreparePrintAsync()) == null && !actualParent.HasDetails && actualParent.Items.Count == 0, "Viewer opened a preview after Print revocation.");
            Await(actualParent.SearchAsync()); Await(actualParent.SelectAsync(actualParent.Items.First()));
            Assert(!actualParent.CanPrint && ((Button)form.FindName("PrintButton")).Visibility == Visibility.Collapsed, "View-only viewer exposed printing.");
            form.Close(); open = null;
            Console.WriteLine("Invoice printing checks passed: snapshots, precision, pagination, cancel/reprint, live rights, busy/failure and real UI entry; no printer used.");
        }
        finally { clock.Block = false; clock.Release.Set(); if (open != null) open.Close(); app.Shutdown(); SQLiteConnection.ClearAllPools(); Directory.Delete(directory, true); }
    }
}
