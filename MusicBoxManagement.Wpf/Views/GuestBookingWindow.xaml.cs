using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using MusicBoxManagement.Wpf.Models;
using MusicBoxManagement.Wpf.Services;
using MusicBoxManagement.Wpf.ViewModels;

namespace MusicBoxManagement.Wpf.Views
{
    public partial class GuestBookingWindow : Window
    {
        private readonly GuestBookingService service;
        private readonly GuestBookingViewModel viewModel;
        public GuestBookingWindow(GuestBookingService service) : this(service, new GuestBookingViewModel(service)) { }
        public GuestBookingWindow(GuestBookingService service, GuestBookingViewModel viewModel)
        {
            InitializeComponent();this.service=service;this.viewModel=viewModel;DataContext=viewModel;
            Closing+=(sender,args)=>{if(viewModel.IsBusy)args.Cancel=true;};
        }
        private async void Window_Loaded(object sender,RoutedEventArgs e)=>await viewModel.RefreshAsync();
        private async void Refresh_Click(object sender,RoutedEventArgs e)=>await viewModel.RefreshAsync();
        private async void Preview_Click(object sender,RoutedEventArgs e)=>await viewModel.PreviewAsync();
        private async void Submit_Click(object sender,RoutedEventArgs e)=>await viewModel.SubmitAsync();
        private async void New_Click(object sender,RoutedEventArgs e){viewModel.StartNew();await viewModel.RefreshAsync();}
        private void Room_Changed(object sender,SelectionChangedEventArgs e)
        {
            RoomImage.Source=null;
            if(!(RoomsTable.SelectedItem is PublicRoom room)){RoomDetails.Text="Chọn phòng để xem ảnh và tiện ích.";return;}
            RoomDetails.Text=room.Amenities+"\n"+room.Description;
            try
            {
                using(var stream=File.OpenRead(service.GetImagePath(room.ImageUrl)))
                {var bitmap=new BitmapImage();bitmap.BeginInit();bitmap.CacheOption=BitmapCacheOption.OnLoad;bitmap.DecodePixelWidth=600;bitmap.StreamSource=stream;bitmap.EndInit();bitmap.Freeze();RoomImage.Source=bitmap;}
            }
            catch(Exception){RoomDetails.Text+="\nChưa đọc được ảnh phòng.";}
        }
    }
}
