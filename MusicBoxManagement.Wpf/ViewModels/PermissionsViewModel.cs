using System;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using MusicBoxManagement.Wpf.Models;
using MusicBoxManagement.Wpf.Services;

namespace MusicBoxManagement.Wpf.ViewModels
{
    public sealed class PermissionMatrixRow : INotifyPropertyChanged
    {
        private bool granted;
        private readonly Func<bool> canChange;
        private readonly Action changed;
        public string Code { get; }
        public string Name { get; }
        public bool CanEdit { get; }
        public string Restriction => CanEdit ? "" : PermissionCatalog.IsAdministrative(Code) ? "Chỉ Admin" : "Staff không được cấp";
        public bool IsGranted
        {
            get => granted;
            set
            {
                if (granted == value || !CanEdit || !canChange()) return;
                granted = value; PropertyChanged?.Invoke(this,new PropertyChangedEventArgs(nameof(IsGranted))); changed();
            }
        }
        internal PermissionMatrixRow(string code,string name,bool allowed,bool value,Func<bool> canChange,Action changed)
        { Code=code;Name=name;CanEdit=allowed;granted=value;this.canChange=canChange;this.changed=changed; }
        public event PropertyChangedEventHandler PropertyChanged;
    }
    public sealed class PermissionsViewModel : INotifyPropertyChanged
    {
        private readonly PermissionManagementService service;
        private readonly LoginSession actor;
        private bool busy, access;
        private string role="Staff",status="Chọn vai trò rồi tải ma trận quyền.";
        private RolePermissionMatrix original;
        private IReadOnlyList<string> pending;
        public PermissionsViewModel(PermissionManagementService service,LoginSession actor)
        { this.service=service;this.actor=actor; }
        public IReadOnlyList<string> Roles { get; } = Array.AsReadOnly(new[] { "Staff","Manager" });
        public string SelectedRole
        {
            get=>role;
            set
            {
                if (busy || role==value || !Roles.Contains(value)) return;
                role=value;Clear();status="Đã đổi vai trò. Bấm Làm mới để đọc quyền hiện hành.";Changed();
            }
        }
        public ObservableCollection<PermissionMatrixRow> Rows { get; } = new ObservableCollection<PermissionMatrixRow>();
        public bool IsBusy=>busy;
        public bool CanReload=>!busy;
        public bool CanInput=>!busy&&access;
        public bool CanSave=>CanInput&&original!=null&&pending==null;
        public bool CanConfirm=>!busy&&access&&pending!=null;
        public bool HasConfirmation=>pending!=null;
        public string Status=>status;
        public string ConfirmationText=>pending==null ? "" : "Thay tập quyền của "+role+" bằng "+pending.Count+" quyền đã chọn? Thay đổi áp dụng ở thao tác tiếp theo của nhân viên. Admin luôn toàn quyền.";
        public event PropertyChangedEventHandler PropertyChanged;
        public async Task ReloadAsync()
        {
            if(busy)return;busy=true;Clear();Changed();
            try { Apply(await Task.Run(()=>service.Read(actor,role)));status="Đã tải quyền "+role+". Chọn/bỏ quyền rồi kiểm tra trước khi lưu."; }
            catch(Exception error){Fail(error);}
            finally {busy=false;Changed();}
        }
        public async Task PrepareAsync()
        {
            if(!CanSave)return;
            var selected=Rows.Where(r=>r.IsGranted).Select(r=>r.Code).ToArray();
            busy=true;Changed();
            try
            {
                var current=await Task.Run(()=>service.Read(actor,role));
                if(current.Revision!=original.Revision)throw new InvalidOperationException("Ma trận quyền đã thay đổi. Hãy tải lại trước khi lưu.");
                pending=PermissionManagementService.ValidateSelection(role,selected);
                status="Kiểm tra nội dung rồi Xác nhận lưu, hoặc Quay lại.";
            }
            catch(ArgumentException error){pending=null;status=error.Message;}
            catch(Exception error){Fail(error);}
            finally{busy=false;Changed();}
        }
        public void KeepEditing()
        {if(busy)return;pending=null;status="Chưa lưu thay đổi. Bạn có thể chỉnh lại các quyền.";Changed();}
        public async Task<bool> ConfirmAsync()
        {
            if(!CanConfirm)return false;
            var observed=original;var codes=pending.ToArray();busy=true;Changed();
            try { await Task.Run(()=>service.Update(actor,observed,codes)); }
            catch(Exception error){Fail(error);busy=false;Changed();return false;}
            Clear();
            var notice="Đã lưu ma trận quyền "+role+". Quyền mới áp dụng ở thao tác tiếp theo.";
            try {Apply(await Task.Run(()=>service.Read(actor,role)));status=notice;}
            catch {Clear();status=notice+" Chưa tải lại được; hãy bấm Làm mới, không gửi lại thao tác vừa lưu.";}
            busy=false;Changed();return true;
        }
        private void Apply(RolePermissionMatrix value)
        {
            original=value;access=true;
            var granted=new HashSet<string>(value.PermissionCodes,StringComparer.Ordinal);
            foreach(var permission in PermissionCatalog.All)
            {
                bool allowed=!PermissionCatalog.IsAdministrative(permission.Key)&&!(role=="Staff"&&permission.Key.StartsWith("Report.",StringComparison.Ordinal));
                Rows.Add(new PermissionMatrixRow(permission.Key,permission.Value,allowed,granted.Contains(permission.Key),()=>CanInput,Edited));
            }
        }
        private void Edited(){pending=null;status="Chưa lưu thay đổi. Bấm Kiểm tra / Lưu để xác nhận.";Changed();}
        private void Clear(){original=null;pending=null;access=false;Rows.Clear();}
        private void Fail(Exception error)
        {
            Clear();status=error is UnauthorizedAccessException ? "Không còn quyền chỉnh ma trận hoặc phiên đã hết hiệu lực. Hãy đóng/đăng nhập lại."
                : error is ArgumentException||error is InvalidOperationException ? error.Message+" Hãy tải lại trước khi thử tiếp."
                : "Chưa hoàn tất thao tác. Hãy tải lại để kiểm tra; dữ liệu cũ đã được xóa.";
        }
        private void Changed(){foreach(var name in new[]{nameof(SelectedRole),nameof(IsBusy),nameof(CanReload),nameof(CanInput),nameof(CanSave),nameof(CanConfirm),nameof(HasConfirmation),nameof(Status),nameof(ConfirmationText)})Notify(name);}
        private void Notify([CallerMemberName]string name=null)=>PropertyChanged?.Invoke(this,new PropertyChangedEventArgs(name));
    }
}
