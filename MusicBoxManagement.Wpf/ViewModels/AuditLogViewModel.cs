using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using MusicBoxManagement.Wpf.Models;
using MusicBoxManagement.Wpf.Services;

namespace MusicBoxManagement.Wpf.ViewModels
{
    public sealed class AuditActorChoice
    {
        public string Code { get; }
        public string Label { get; }
        internal AuditActorChoice(string code,string label){Code=code;Label=label;}
    }
    public sealed class AuditLogViewModel : INotifyPropertyChanged
    {
        private readonly IAuditLogService service;
        private readonly LoginSession actor;
        private bool busy,access,more;
        private int? cursor;
        private DateTime? from,to;
        private string actorType,action,entity,status="Đang tải nhật ký…";
        private AuditLogEntry selected,detail;
        public AuditLogViewModel(IAuditLogService service,LoginSession actor){this.service=service;this.actor=actor;}
        public IReadOnlyList<AuditActorChoice> ActorTypes { get; }=Array.AsReadOnly(new[]{new AuditActorChoice(null,"Tất cả"),new AuditActorChoice("Staff","Nhân viên"),new AuditActorChoice("Guest","Khách"),new AuditActorChoice("System","Hệ thống")});
        public ObservableCollection<AuditLogEntry> Items { get; }=new ObservableCollection<AuditLogEntry>();
        public DateTime? FromDate {get=>from;set{if(busy||from==value)return;from=value;FilterChanged();}}
        public DateTime? ToDate {get=>to;set{if(busy||to==value)return;to=value;FilterChanged();}}
        public string ActorType {get=>actorType;set{if(busy||actorType==value)return;actorType=value;FilterChanged();}}
        public string ActionQuery {get=>action;set{if(busy||action==value)return;action=value;FilterChanged();}}
        public string EntityQuery {get=>entity;set{if(busy||entity==value)return;entity=value;FilterChanged();}}
        public AuditLogEntry Selected=>selected;
        public AuditLogEntry Detail=>detail;
        public bool HasDetails=>detail!=null;
        public bool IsBusy=>busy;
        public bool CanSearch=>!busy;
        public bool CanSelect=>!busy&&access;
        public bool CanNextPage=>!busy&&access&&more&&cursor.HasValue;
        public string Status=>status;
        public string DetailText=>detail==null?"":"Nhật ký #"+detail.AuditLogId+" — "+detail.CreatedLabel
            +"\nNgười thực hiện: "+detail.ActorLabel+"\nTài khoản hiện hành: "+detail.UserLabel
            +"\nUserId: "+(detail.UserId??"—")+"\nHành động: "+detail.Action
            +"\nĐối tượng: "+detail.EntityName+" / "+(detail.EntityId??"—")+"\n\n"+detail.Description;
        public event PropertyChangedEventHandler PropertyChanged;
        public Task SearchAsync()=>LoadAsync(null);
        public Task NextPageAsync()=>CanNextPage?LoadAsync(cursor):Task.CompletedTask;
        private async Task LoadAsync(int? before)
        {
            if(busy)return;
            var start=from;var end=to;var kind=actorType;var actionFilter=action;var entityFilter=entity;
            busy=true;Clear();Changed();
            try
            {
                var page=await Task.Run(()=>service.Search(actor,start,end,kind,actionFilter,entityFilter,before));
                foreach(var item in page.Items)Items.Add(item);
                more=page.HasMore;cursor=page.NextBeforeId;access=true;
                status="Đã tải "+Items.Count+" bản ghi"+(before.HasValue?" ở trang cũ hơn":" mới nhất theo bộ lọc")
                    +". Cập nhật lúc "+page.CheckedAt.ToOffset(TimeSpan.FromHours(7)).ToString("HH:mm:ss dd/MM/yyyy",CultureInfo.GetCultureInfo("vi-VN"))
                    +"; chọn dòng để đọc chi tiết."+(more?" Còn bản ghi ở trang tiếp theo.":"");
            }
            catch(Exception error){Fail(error);}
            finally{busy=false;Changed();}
        }
        public async Task SelectAsync(AuditLogEntry value)
        {
            if(!CanSelect||selected==value)return;
            selected=value;detail=null;busy=value!=null;Changed();
            if(value==null)return;
            try
            {
                detail=await Task.Run(()=>service.Read(actor,value.AuditLogId));
                status="Đã đọc nhật ký #"+detail.AuditLogId+". Chỉ xem, không sửa/xóa.";
            }
            catch(Exception error){Fail(error);}
            finally{busy=false;Changed();}
        }
        public void ClearFilters()
        {if(busy)return;from=to=null;actorType=action=entity=null;FilterChanged();}
        private void FilterChanged(){Clear();status="Bộ lọc đã đổi. Bấm Tìm / Cập nhật để tải lại nhật ký.";Changed();}
        private void Clear(){selected=detail=null;cursor=null;more=access=false;Items.Clear();}
        private void Fail(Exception error)
        {
            Clear();
            if(error is UnauthorizedAccessException)
            {from=to=null;actorType=action=entity=null;status="Không còn quyền xem nhật ký hoặc phiên đã hết hiệu lực. Hãy đóng/đăng nhập lại.";}
            else status=error is ArgumentException||error is InvalidOperationException?error.Message+" Hãy tải lại.":"Chưa đọc được nhật ký. Dữ liệu cũ đã được xóa; hãy thử tải lại.";
        }
        private void Changed(){foreach(var name in new[]{nameof(FromDate),nameof(ToDate),nameof(ActorType),nameof(ActionQuery),nameof(EntityQuery),nameof(Selected),nameof(Detail),nameof(DetailText),nameof(HasDetails),nameof(IsBusy),nameof(CanSearch),nameof(CanSelect),nameof(CanNextPage),nameof(Status)})Notify(name);}
        private void Notify([CallerMemberName]string name=null)=>PropertyChanged?.Invoke(this,new PropertyChangedEventArgs(name));
    }
}
