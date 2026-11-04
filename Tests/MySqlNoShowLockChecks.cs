using System;
using System.Globalization;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MusicBoxManagement.Wpf.Data;
using MusicBoxManagement.Wpf.Models;
using MusicBoxManagement.Wpf.Services;
public static class MusicBoxMySqlNoShowLockChecks
{
 private sealed class Clock:IClock {public DateTimeOffset Now;public DateTimeOffset UtcNow{get{return Now;}}}
 private sealed class HookClock:IClock {public Func<DateTimeOffset> Read;public DateTimeOffset UtcNow{get{return Read();}}}
 private static DateTimeOffset At(int h,int m=0){return new DateTimeOffset(2026,11,4,h,m,0,TimeSpan.FromHours(7));}
 private static void Check(bool ok,string message){if(!ok)throw new Exception(message);}
 private static object Sql(MySqlDatabase db,string sql){using(var c=db.OpenConnection())using(var q=c.CreateCommand()){q.CommandText=sql;return q.ExecuteScalar();}}
 private static Reservation Book(MySqlDatabase db,Clock clock,string phone,int hour=10){return new MySqlReservationService(db,clock).CreateGuest(new ReservationRequest{RoomId=1,FullName="Khách test",PhoneNumber=phone,StartTime=At(hour),DurationMinutes=60});}
 public static void Run()
 {
  var name="musicbox_test_"+Guid.NewGuid().ToString("N");Console.WriteLine("NoShow lock fixture: "+name);var db=new MySqlDatabase(name);var clock=new Clock{Now=At(9)};
  try {
   var admin=new MySqlAuthenticationService(db).SetupAdminAsync("admin","Admin test","NoShow-Locks-2026!").GetAwaiter().GetResult();
   Sql(db,"INSERT INTO Rooms(RoomId,RoomCode,RoomTypeId,Name,ImageUrl,IsActive,CreatedAt) VALUES(1,'S01',1,'Phòng test','Content/uploads/rooms/11111111111111111111111111111111.png',1,'"+clock.Now.ToUniversalTime().ToString("O",CultureInfo.InvariantCulture)+"');");
   var old=Book(db,clock,"0123456789");clock.Now=At(10,15);Check(new MySqlNoShowService(db,clock).ProcessExpired()==1,"NoShow fixture was not made through real service.");
   var entered=new ManualResetEventSlim();Exception failure=null;Task<int> probe;
   using(var c=db.OpenConnection())using(var tx=MySqlDatabase.BeginWriteTransaction(c))using(var q=c.CreateCommand()) {
    q.Transaction=tx;q.CommandText="SELECT ReservationId FROM Reservations WHERE ReservationId="+old.ReservationId+" FOR UPDATE";q.ExecuteScalar();
    probe=Task.Run(()=>{try{return new MySqlNoShowService(db,new HookClock{Read=()=>{entered.Set();return clock.Now;}}).ProcessExpired();}catch(Exception e){failure=e;return -1;}});
    var reached=entered.Wait(3500);Console.WriteLine("Clock reached while terminal row locked: "+reached);tx.Commit();var result=probe.GetAwaiter().GetResult();
    Check(reached&&failure==null&&result==0,"RED: NoShow worker blocked on a terminal history row although there were no Confirmed bookings.");
   }
   Check(Convert.ToInt32(Sql(db,"SELECT COUNT(*) FROM AuditLog WHERE Action='Reservation.NoShow'"))==1,"No-op worker changed audit.");
   // A real Confirmed source must still serialize with maintenance, then use a fresh clock.
   clock.Now=At(9);var current=Book(db,clock,"0123456788",11);var called=new ManualResetEventSlim();var reads=0;Task<int> waiting;
   using(var c=db.OpenConnection())using(var tx=MySqlDatabase.BeginWriteTransaction(c))using(var q=c.CreateCommand()) {
    q.Transaction=tx;q.CommandText="SELECT ReservationId FROM Reservations WHERE ReservationId="+current.ReservationId+" FOR UPDATE";q.ExecuteScalar();
    waiting=Task.Run(()=>new MySqlNoShowService(db,new HookClock{Read=()=>{Interlocked.Increment(ref reads);called.Set();return At(11,15);}}).ProcessExpired());
    var early=called.Wait(300);tx.Commit();Check(waiting.GetAwaiter().GetResult()==1&&!early&&reads==1,"Confirmed lock or post-wait clock was bypassed.");
   }
   Check(Convert.ToString(Sql(db,"SELECT Status FROM Reservations WHERE ReservationId="+current.ReservationId))=="NoShow","Fresh clock did not expire source.");
   // Stale worker discovery can include A just before check-in changes A to CheckedIn.
   // Gate that worker's first PK lock A; an extension must wait on A before locking B.
   clock.Now=At(9);var a=Book(db,clock,"0123456787",10);var b=Book(db,clock,"0123456786",11);
   var active=new MySqlRoomSessionService(db,clock).CheckIn(admin,a.ReservationId);var wrongOrder=false;Task<RoomSession> extension;
   using(var c=db.OpenConnection())using(var tx=MySqlDatabase.BeginWriteTransaction(c))using(var q=c.CreateCommand()) {
    q.Transaction=tx;q.CommandText="SELECT ReservationId FROM Reservations WHERE ReservationId="+a.ReservationId+" FOR UPDATE";q.ExecuteScalar();
    extension=Task.Run(()=>new MySqlRoomSessionService(db,clock).ExtendStaff(admin,active.RoomSessionId,active.ExpectedEndTime.Value,30));
    try {
     var waitingOnA=0;var watch=System.Diagnostics.Stopwatch.StartNew();
     while(waitingOnA==0&&!extension.IsCompleted&&watch.Elapsed.TotalMilliseconds<15000){Thread.Sleep(50);waitingOnA=Convert.ToInt32(Sql(db,"SELECT COUNT(*) FROM performance_schema.data_lock_waits w JOIN performance_schema.data_locks l ON l.ENGINE_LOCK_ID=w.REQUESTING_ENGINE_LOCK_ID WHERE l.OBJECT_SCHEMA='"+name+"' AND l.OBJECT_NAME='Reservations' AND l.LOCK_DATA='"+a.ReservationId+"'"));}
     if(waitingOnA==0)Console.WriteLine("Extension gate diagnostic: "+extension.Status+" "+(extension.Exception==null?"":extension.Exception.GetBaseException().Message));
     Check(waitingOnA>0,"Extension did not reach gated source lock.");
     wrongOrder=Convert.ToInt32(Sql(db,"SELECT COUNT(*) FROM performance_schema.data_locks WHERE OBJECT_SCHEMA='"+name+"' AND OBJECT_NAME='Reservations' AND INDEX_NAME='PRIMARY' AND LOCK_DATA='"+b.ReservationId+"' AND LOCK_STATUS='GRANTED' AND LOCK_MODE LIKE 'X%'"))>0;
     if(!wrongOrder){q.CommandText="SELECT ReservationId FROM Reservations WHERE ReservationId="+b.ReservationId+" FOR UPDATE";q.ExecuteScalar();}
    } finally{tx.Commit();}
   }
   var extended=extension.GetAwaiter().GetResult();Check(!wrongOrder&&extended.ExpectedEndTime==active.ExpectedEndTime.Value.AddMinutes(30),"RED review: extension locked later candidate B before its CheckedIn source A; stale worker can deadlock.");
   // Cover both sides of the 128-statement batch boundary with valid independent resources.
   // SQL here only constructs Confirmed fixtures; no fake session, payment or invoice.
   var seed=new StringBuilder();var stamp=At(9).ToUniversalTime().ToString("O",CultureInfo.InvariantCulture);var start=At(11).ToUniversalTime().ToString("O",CultureInfo.InvariantCulture);var end=At(12).ToUniversalTime().ToString("O",CultureInfo.InvariantCulture);
   for(var i=2;i<=131;i++)seed.Append("INSERT INTO Rooms(RoomId,RoomCode,RoomTypeId,Name,ImageUrl,IsActive,CreatedAt) VALUES("+i+",'S"+i+"',1,'Batch room','Content/uploads/rooms/11111111111111111111111111111111.png',1,'"+stamp+"');INSERT INTO Customers(CustomerId,FullName,PhoneNumber) VALUES("+(1000+i)+",'Batch customer','"+(1000000+i).ToString("D10")+"');INSERT INTO Reservations(RoomId,CustomerId,StartTime,EndTime,Status,CreatedAt) VALUES("+i+","+(1000+i)+",'"+start+"','"+end+"','Confirmed','"+stamp+"');");
   Sql(db,seed.ToString());var lastId=Convert.ToInt32(Sql(db,"SELECT MAX(ReservationId) FROM Reservations"));var gate=new ManualResetEventSlim();var release=new ManualResetEventSlim();
   var worker=Task.Run(()=>new MySqlNoShowService(db,new HookClock{Read=()=>{gate.Set();if(!release.Wait(15000))throw new Exception("Batch lock gate timed out");return At(11);}}).ProcessExpired());
   try{Check(gate.Wait(15000),"Batch did not reach post-lock clock.");var locked=Convert.ToInt32(Sql(db,"SELECT COUNT(*) FROM performance_schema.data_locks WHERE OBJECT_SCHEMA='"+name+"' AND OBJECT_NAME='Reservations' AND INDEX_NAME='PRIMARY' AND LOCK_DATA='"+lastId+"' AND LOCK_STATUS='GRANTED' AND LOCK_MODE='X,REC_NOT_GAP'"));Check(locked==1,"Last candidate after batch boundary lacks exact record lock.");}
   finally{release.Set();}Check(worker.GetAwaiter().GetResult()==0,"Future batch expired prematurely.");
   Check(Convert.ToInt32(Sql(db,"SELECT COUNT(*) FROM Reservations WHERE Status='Confirmed'"))==131,"Batch mutated future bookings.");
   Check(Convert.ToInt32(Sql(db,"SELECT COUNT(*) FROM AuditLog WHERE Action='Reservation.NoShow'"))==2,"Batch no-op added audit.");
   Console.WriteLine("PASS NoShow point locks: terminal history does not block; Confirmed still waits/fresh clock; source/candidate lock order; >128 candidates/batch boundary/REC_NOT_GAP; real transition and idempotence.");
  }finally{using(var c=new MySqlDatabase("defaultdb").OpenConnection())using(var q=c.CreateCommand()){q.CommandText="DROP DATABASE IF EXISTS `"+name+"`";q.ExecuteNonQuery();}}
 }
}
