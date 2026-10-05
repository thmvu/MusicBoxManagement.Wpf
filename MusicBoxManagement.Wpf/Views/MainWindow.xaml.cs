using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using MusicBoxManagement.Wpf.Models;
using MusicBoxManagement.Wpf.Services;
using MusicBoxManagement.Wpf.ViewModels;

namespace MusicBoxManagement.Wpf.Views
{
    public partial class MainWindow : Window
    {
        private readonly MainViewModel viewModel;
        private readonly AuthenticationService authentication;
        private readonly PermissionService permissions;
        private readonly RoomTypeService roomTypes;
        private readonly RoomService rooms;
        private readonly ServiceCatalogService services;
        private bool isOpeningServices;
        private readonly CustomerService customers;
        private bool isOpeningCustomers;
        private readonly GuestBookingService guestBooking;
        private bool isOpeningBooking;
        private readonly GuestReservationService guestLookup;
        private bool isOpeningLookup;
        private LoginSession session;
        private bool canEditRoomTypes;
        private bool isOpeningEditor;
        private bool isOpeningRooms;

        public MainWindow(MainViewModel viewModel, AuthenticationService authentication, PermissionService permissions, RoomTypeService roomTypes, RoomService rooms, ServiceCatalogService services, CustomerService customers, GuestBookingService guestBooking, GuestReservationService guestLookup)
        {
            InitializeComponent();
            this.viewModel = viewModel;
            this.authentication = authentication;
            this.permissions = permissions;
            this.roomTypes = roomTypes;
            this.rooms = rooms;
            this.services = services;
            this.customers = customers;
            this.guestBooking = guestBooking;
            this.guestLookup = guestLookup;
            DataContext = viewModel;
        }

        private async void Window_Loaded(object sender, RoutedEventArgs e)
        {
            await viewModel.RefreshAsync();
            await PrepareLoginAsync();
        }

        private async void Refresh_Click(object sender, RoutedEventArgs e)
        {
            await viewModel.RefreshAsync();
            if (session == null) await PrepareLoginAsync();
            else
            {
                await RefreshAccessAsync();
                if (session != null && canEditRoomTypes) ShowCatalog();
            }
        }

        private async Task PrepareLoginAsync()
        {
            LoginButton.IsEnabled = false;
            try
            {
                var needsSetup = await Task.Run(() => authentication.NeedsSetup());
                LoginButton.Content = needsSetup ? "Thiết lập Admin" : "Đăng nhập nhân viên";
                LoginButton.IsEnabled = true;
            }
            catch (Exception) { ModeText.Text = "Chưa đọc được tài khoản. Hãy thử làm mới."; }
        }

        private async void Login_Click(object sender, RoutedEventArgs e)
        {
            LoginButton.IsEnabled = false;
            try
            {
                var needsSetup = await Task.Run(() => authentication.NeedsSetup());
                var dialog = new AuthenticationWindow(authentication, needsSetup) { Owner = this };
                if (dialog.ShowDialog() == true)
                {
                    session = dialog.Session;
                    await RefreshAccessAsync();
                }
            }
            catch (Exception) { MessageBox.Show(this, "Không đọc được tài khoản. Hãy thử làm mới.", "Music Box"); }
            finally { await PrepareLoginAsync(); }
        }

        private async void RefreshAccess_Click(object sender, RoutedEventArgs e) => await RefreshAccessAsync();

        private async Task RefreshAccessAsync()
        {
            var currentSession = session;
            if (currentSession == null) return;
            RefreshAccessButton.IsEnabled = false;
            try
            {
                var access = await Task.Run(() => permissions.GetStaffAccess(currentSession));
                if (session != currentSession) return;
                StaffIdentity.Text = access.FullName + " — " + access.Role;
                PermissionsTable.ItemsSource = access.Permissions;
                canEditRoomTypes = access.Permissions.ContainsKey("RoomType.Edit");
                RoomTypesButton.Visibility = canEditRoomTypes ? Visibility.Visible : Visibility.Collapsed;
                RoomsButton.Visibility = access.Permissions.ContainsKey("Room.Manage") ? Visibility.Visible : Visibility.Collapsed;
                ServicesButton.Visibility = access.Permissions.ContainsKey("Service.Manage") ? Visibility.Visible : Visibility.Collapsed;
                CustomersButton.Visibility = access.Permissions.ContainsKey("Customer.View") ? Visibility.Visible : Visibility.Collapsed;
                EditRoomTypeButton.Visibility = Visibility.Collapsed;
                BackToStaffButton.Visibility = Visibility.Collapsed;
                ModeText.Text = "Nhân viên: " + access.Role;
                CurrentAreaText.Text = "Khu vực nhân viên";
                StaffPanel.Visibility = Visibility.Visible;
                GuestPanel.Visibility = Visibility.Collapsed;
                BookingButton.Visibility = Visibility.Collapsed;
                LookupButton.Visibility = Visibility.Collapsed;
                LoginButton.Visibility = Visibility.Collapsed;
                LogoutButton.Visibility = Visibility.Visible;
                AccessStatus.Text = "Đã kiểm tra " + access.Permissions.Count + " quyền hiện hành. Các màn hình nghiệp vụ sẽ làm ở bước tiếp theo.";
            }
            catch (UnauthorizedAccessException error)
            {
                if (session != currentSession) return;
                ReturnToGuest();
                MessageBox.Show(this, error.Message, "Music Box");
            }
            catch (Exception)
            {
                if (session != currentSession) return;
                ReturnToGuest();
                MessageBox.Show(this, "Không kiểm tra được quyền. Hãy đăng nhập lại khi dữ liệu sẵn sàng.", "Music Box");
            }
            finally { RefreshAccessButton.IsEnabled = true; }
        }

        private async void Logout_Click(object sender, RoutedEventArgs e)
        {
            ReturnToGuest();
            await viewModel.RefreshAsync();
            await PrepareLoginAsync();
        }

        private void ReturnToGuest()
        {
            authentication.Logout(session);
            session = null;
            canEditRoomTypes = false;
            EditRoomTypeButton.Visibility = BackToStaffButton.Visibility = RoomTypesButton.Visibility = Visibility.Collapsed;
            RoomsButton.Visibility = Visibility.Collapsed;
            ServicesButton.Visibility = Visibility.Collapsed;
            CustomersButton.Visibility = Visibility.Collapsed;
            PermissionsTable.ItemsSource = null;
            StaffIdentity.Text = "";
            ModeText.Text = "Chế độ Khách";
            CurrentAreaText.Text = "Loại phòng";
            StaffPanel.Visibility = Visibility.Collapsed;
            GuestPanel.Visibility = Visibility.Visible;
            BookingButton.Visibility = Visibility.Visible;
            LookupButton.Visibility = Visibility.Visible;
            LoginButton.Visibility = Visibility.Visible;
            LogoutButton.Visibility = Visibility.Collapsed;
        }

        private async void ShowRoomTypes_Click(object sender, RoutedEventArgs e)
        {
            await RefreshAccessAsync();
            if (session == null || !canEditRoomTypes) return;
            await viewModel.RefreshAsync();
            ShowCatalog();
        }

        private async void ShowRooms_Click(object sender, RoutedEventArgs e)
        {
            if (isOpeningRooms) return;
            isOpeningRooms = true;
            RoomsButton.IsEnabled = false;
            try
            {
                var currentSession = session;
                await Task.Run(() => permissions.Demand(currentSession, "Room.Manage"));
                if (session != currentSession) return;
                new RoomsWindow(rooms, currentSession, roomTypes) { Owner = this }.ShowDialog();
            }
            catch (UnauthorizedAccessException) { MessageBox.Show(this, "Bạn không có quyền quản lý phòng hoặc phiên đã hết hiệu lực.", "Music Box"); }
            catch (Exception) { MessageBox.Show(this, "Không mở được danh mục phòng. Hãy thử lại.", "Music Box"); }
            finally
            {
                isOpeningRooms = false;
                RoomsButton.IsEnabled = true;
                await RefreshAccessAsync();
            }
        }

        private void ShowCatalog()
        {
            if (session == null || !canEditRoomTypes) return;
            StaffPanel.Visibility = Visibility.Collapsed;
            GuestPanel.Visibility = Visibility.Visible;
            CurrentAreaText.Text = "Loại phòng";
            EditRoomTypeButton.Visibility = BackToStaffButton.Visibility = Visibility.Visible;
            if (RoomTypesTable.SelectedItem == null && RoomTypesTable.Items.Count > 0) RoomTypesTable.SelectedIndex = 0;
            UpdateEditButton();
        }

        private async void ShowServices_Click(object sender, RoutedEventArgs e)
        {
            if (isOpeningServices) return;
            isOpeningServices = true;
            ServicesButton.IsEnabled = false;
            try
            {
                var currentSession = session;
                await Task.Run(() => permissions.Demand(currentSession, "Service.Manage"));
                if (session != currentSession) return;
                new ServicesWindow(services, currentSession) { Owner = this }.ShowDialog();
            }
            catch (UnauthorizedAccessException) { MessageBox.Show(this, "Bạn không có quyền quản lý dịch vụ hoặc phiên đã hết hiệu lực.", "Music Box"); }
            catch (Exception) { MessageBox.Show(this, "Không mở được dịch vụ. Hãy thử lại.", "Music Box"); }
            finally { isOpeningServices = false; ServicesButton.IsEnabled = true; await RefreshAccessAsync(); }
        }

        private async void ShowCustomers_Click(object sender, RoutedEventArgs e)
        {
            if (isOpeningCustomers) return;
            isOpeningCustomers = true; CustomersButton.IsEnabled = false;
            try
            {
                var currentSession = session;
                await Task.Run(() => permissions.Demand(currentSession, "Customer.View"));
                if (session != currentSession) return;
                new CustomersWindow(customers, currentSession) { Owner = this }.ShowDialog();
            }
            catch (UnauthorizedAccessException) { MessageBox.Show(this, "Bạn không có quyền xem khách hàng hoặc phiên đã hết hiệu lực.", "Music Box"); }
            catch (Exception) { MessageBox.Show(this, "Không mở được khách hàng. Hãy thử lại.", "Music Box"); }
            finally { isOpeningCustomers = false; CustomersButton.IsEnabled = true; await RefreshAccessAsync(); }
        }

        private void ShowBooking_Click(object sender, RoutedEventArgs e)
        {
            if(session!=null || isOpeningBooking)return;
            isOpeningBooking=true;BookingButton.IsEnabled=false;
            try{new GuestBookingWindow(guestBooking){Owner=this}.ShowDialog();}
            finally{isOpeningBooking=false;BookingButton.IsEnabled=true;}
        }

        private void ShowLookup_Click(object sender,RoutedEventArgs args)
        {
            if(session!=null || isOpeningLookup)return;
            isOpeningLookup=true;LookupButton.IsEnabled=false;
            try{new GuestLookupWindow(guestLookup){Owner=this}.ShowDialog();}
            finally{isOpeningLookup=false;LookupButton.IsEnabled=true;}
        }

        private void RoomType_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateEditButton();

        private void UpdateEditButton()
        {
            if (EditRoomTypeButton != null)
                EditRoomTypeButton.IsEnabled = canEditRoomTypes && !isOpeningEditor && RoomTypesTable.SelectedItem is RoomType;
        }

        private async void EditRoomType_Click(object sender, RoutedEventArgs e)
        {
            if (isOpeningEditor || !(RoomTypesTable.SelectedItem is RoomType selected)) return;
            isOpeningEditor = true;
            UpdateEditButton();
            try
            {
                var currentSession = session;
                var original = await Task.Run(() => roomTypes.GetForEdit(currentSession, selected.RoomTypeId));
                if (session != currentSession) return;
                var editor = new RoomTypeEditWindow(new RoomTypeEditViewModel(roomTypes, currentSession, original)) { Owner = this };
                if (editor.ShowDialog() == true) await viewModel.RefreshAsync();
            }
            catch (UnauthorizedAccessException) { MessageBox.Show(this, "Bạn không có quyền sửa loại phòng hoặc phiên đã hết hiệu lực.", "Music Box"); }
            catch (Exception) { MessageBox.Show(this, "Không đọc được loại phòng. Hãy làm mới và thử lại.", "Music Box"); }
            finally
            {
                isOpeningEditor = false;
                await RefreshAccessAsync();
                if (session != null && canEditRoomTypes) ShowCatalog();
                UpdateEditButton();
            }
        }
    }
}
