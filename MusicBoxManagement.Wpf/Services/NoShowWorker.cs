using System;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows.Threading;
using MusicBoxManagement.Wpf.Data;

namespace MusicBoxManagement.Wpf.Services
{
    public sealed class NoShowWorker : IDisposable
    {
        private readonly SqliteDatabase database;
        private readonly NoShowService service;
        private readonly DispatcherTimer timer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(1) };
        private bool running, disposed;
        public NoShowWorker(SqliteDatabase database) : this(database, new SystemClock()) { }
        public NoShowWorker(SqliteDatabase database, IClock clock)
        { this.database = database; service = new NoShowService(database, clock); timer.Tick += Tick; }
        // Call from the UI dispatcher. SQLite work runs in the background.
        public async Task StartAsync()
        { if (disposed) return; timer.Start(); await RunAsync(); }
        private async void Tick(object sender, EventArgs args) => await RunAsync();
        private async Task RunAsync()
        {
            if (running || disposed) return; running = true;
            try { await Task.Run(() => { database.Initialize(); service.ProcessExpired(); }); }
            catch (Exception error) { Trace.TraceError("NoShow chưa xử lý được; sẽ thử lại ở nhịp kế tiếp: {0}", error.Message); }
            finally { running = false; }
        }
        public void Dispose() { disposed = true; timer.Stop(); timer.Tick -= Tick; }
    }
}
