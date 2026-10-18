using System;
using System.Threading.Tasks;
using MusicBoxManagement.Wpf.Models;

namespace MusicBoxManagement.Wpf.ViewModels
{
    internal static class ReportExportWorkflow
    {
        internal static async Task<bool> RunAsync(Func<ReportExcelFile> create, Func<string, string> choosePath)
        {
            var preview = await Task.Run(create);
            var path = choosePath(preview.FileName);
            if (string.IsNullOrWhiteSpace(path)) return false;
            // The second call queries and authorizes after the user closes the dialog.
            await Task.Run(() => create().SaveTo(path));
            return true;
        }
    }
}
