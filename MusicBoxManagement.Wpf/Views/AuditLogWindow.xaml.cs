using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using MusicBoxManagement.Wpf.Models;
using MusicBoxManagement.Wpf.ViewModels;
namespace MusicBoxManagement.Wpf.Views
{
 public partial class AuditLogWindow:Window
 {
  private readonly AuditLogViewModel model;
  public AuditLogWindow(AuditLogViewModel model){InitializeComponent();this.model=model;DataContext=model;}
  private async void Window_Loaded(object sender,RoutedEventArgs args){await model.SearchAsync();}
  private async void Search_Click(object sender,RoutedEventArgs args){await model.SearchAsync();}
  private async void Next_Click(object sender,RoutedEventArgs args){await model.NextPageAsync();}
  private void Clear_Click(object sender,RoutedEventArgs args){model.ClearFilters();}
  private async void Selection_Changed(object sender,SelectionChangedEventArgs args){if(model!=null)await model.SelectAsync(AuditTable.SelectedItem as AuditLogEntry);}
  private void Close_Click(object sender,RoutedEventArgs args){if(!model.IsBusy)Close();}
  protected override void OnClosing(CancelEventArgs args){if(model.IsBusy)args.Cancel=true;base.OnClosing(args);}
 }
}
