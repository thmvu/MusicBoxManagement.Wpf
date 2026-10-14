-- Baseline: SQLite v8 business schema. InnoDB, UTF-8, case-sensitive NO PAD comparisons.
-- UTC timestamps stay as 33-character round-trip strings: DATETIME(6) would lose a .NET tick.
CREATE TABLE IF NOT EXISTS RoomTypes (
 RoomTypeId INT PRIMARY KEY AUTO_INCREMENT,
 Code VARCHAR(20) NOT NULL UNIQUE CHECK(Code IN ('STANDARD','VIP')),
 Name VARCHAR(100) NOT NULL CHECK(CHAR_LENGTH(TRIM(Name)) BETWEEN 1 AND 100),
 Capacity INT NOT NULL CHECK(Capacity>0),
 PricePerHour BIGINT NOT NULL CHECK(PricePerHour>0),
 Amenities VARCHAR(1000) NOT NULL CHECK(CHAR_LENGTH(TRIM(Amenities)) BETWEEN 1 AND 1000),
 Description VARCHAR(2000) NULL
) ENGINE=InnoDB;
-- statement
CREATE TABLE IF NOT EXISTS AspNetUsers (
 Id VARCHAR(128) PRIMARY KEY,
 UserName VARCHAR(100) NOT NULL CHECK(CHAR_LENGTH(TRIM(UserName)) BETWEEN 1 AND 100),
 NormalizedUserName VARCHAR(100) NOT NULL UNIQUE,
 PasswordHash TEXT NOT NULL CHECK(CHAR_LENGTH(PasswordHash)>0),
 SecurityStamp TEXT NOT NULL CHECK(CHAR_LENGTH(SecurityStamp)>0),
 FullName VARCHAR(100) NOT NULL CHECK(CHAR_LENGTH(TRIM(FullName)) BETWEEN 1 AND 100),
 IsActive TINYINT NOT NULL CHECK(IsActive IN (0,1))
) ENGINE=InnoDB;
-- statement
CREATE TABLE IF NOT EXISTS AspNetRoles (
 Id VARCHAR(128) PRIMARY KEY,
 Name VARCHAR(20) NOT NULL UNIQUE CHECK(Name IN ('Staff','Manager','Admin'))
) ENGINE=InnoDB;
-- statement
CREATE TABLE IF NOT EXISTS AspNetUserRoles (
 UserId VARCHAR(128) PRIMARY KEY,
 RoleId VARCHAR(128) NOT NULL,
 FOREIGN KEY(UserId) REFERENCES AspNetUsers(Id),
 FOREIGN KEY(RoleId) REFERENCES AspNetRoles(Id),
 INDEX IX_AspNetUserRoles_RoleId(RoleId)
) ENGINE=InnoDB;
-- statement
CREATE TABLE IF NOT EXISTS Permission (
 PermissionId INT PRIMARY KEY AUTO_INCREMENT,
 Code VARCHAR(100) NOT NULL UNIQUE,
 Name VARCHAR(200) NOT NULL
) ENGINE=InnoDB;
-- statement
CREATE TABLE IF NOT EXISTS RolePermission (
 RoleId VARCHAR(128) NOT NULL,
 PermissionId INT NOT NULL,
 PRIMARY KEY(RoleId,PermissionId),
 FOREIGN KEY(RoleId) REFERENCES AspNetRoles(Id),
 FOREIGN KEY(PermissionId) REFERENCES Permission(PermissionId)
) ENGINE=InnoDB;
-- statement
CREATE TABLE IF NOT EXISTS AuditLog (
 AuditLogId INT PRIMARY KEY AUTO_INCREMENT,
 ActorType VARCHAR(20) NOT NULL CHECK(ActorType IN ('Staff','Guest','System')),
 UserId VARCHAR(128) NULL,
 Action VARCHAR(100) NOT NULL,
 EntityName VARCHAR(100) NOT NULL,
 EntityId VARCHAR(200) NULL,
 Description TEXT NOT NULL,
 CreatedAt VARCHAR(33) NOT NULL,
 FOREIGN KEY(UserId) REFERENCES AspNetUsers(Id)
) ENGINE=InnoDB;
-- statement
CREATE TABLE IF NOT EXISTS Rooms (
 RoomId INT PRIMARY KEY AUTO_INCREMENT,
 RoomCode VARCHAR(50) COLLATE utf8mb4_0900_as_ci NOT NULL UNIQUE CHECK(CHAR_LENGTH(TRIM(RoomCode)) BETWEEN 1 AND 50),
 RoomTypeId INT NOT NULL,
 Name VARCHAR(100) NOT NULL CHECK(CHAR_LENGTH(TRIM(Name)) BETWEEN 1 AND 100),
 ImageUrl VARCHAR(500) NOT NULL CHECK(CHAR_LENGTH(TRIM(ImageUrl)) BETWEEN 1 AND 500),
 Description VARCHAR(2000) NULL,
 IsActive TINYINT NOT NULL CHECK(IsActive IN (0,1)),
 InactiveReason VARCHAR(1000) NULL,
 CreatedAt VARCHAR(33) NOT NULL,
 CHECK(IsActive=1 OR CHAR_LENGTH(TRIM(COALESCE(InactiveReason,'')))>0),
 FOREIGN KEY(RoomTypeId) REFERENCES RoomTypes(RoomTypeId)
) ENGINE=InnoDB;
-- statement
CREATE TABLE IF NOT EXISTS Customers (
 CustomerId INT PRIMARY KEY AUTO_INCREMENT,
 FullName VARCHAR(100) NOT NULL CHECK(CHAR_LENGTH(TRIM(FullName)) BETWEEN 1 AND 100),
 PhoneNumber VARCHAR(10) NOT NULL UNIQUE CHECK(REGEXP_LIKE(PhoneNumber,'^0[0-9]{9}$','c'))
) ENGINE=InnoDB;
-- statement
CREATE TABLE IF NOT EXISTS Reservations (
 ReservationId INT PRIMARY KEY AUTO_INCREMENT,
 CustomerId INT NOT NULL,
 RoomId INT NOT NULL,
 StartTime VARCHAR(33) NOT NULL CHECK(REGEXP_LIKE(StartTime,'^[0-9]{4}-(0[1-9]|1[0-2])-(0[1-9]|[12][0-9]|3[01])T([01][0-9]|2[0-3]):[0-5][0-9]:[0-5][0-9][.][0-9]{7}[+]00:00$','c')),
 EndTime VARCHAR(33) NOT NULL CHECK(REGEXP_LIKE(EndTime,'^[0-9]{4}-(0[1-9]|1[0-2])-(0[1-9]|[12][0-9]|3[01])T([01][0-9]|2[0-3]):[0-5][0-9]:[0-5][0-9][.][0-9]{7}[+]00:00$','c')),
 Status VARCHAR(20) NOT NULL CHECK(Status IN ('Confirmed','CheckedIn','Completed','Cancelled','NoShow')),
 CancellationReason TEXT NULL,
 CreatedByUserId VARCHAR(128) NULL,
 CreatedAt VARCHAR(33) NOT NULL,
 CHECK(StartTime<EndTime),
 FOREIGN KEY(CustomerId) REFERENCES Customers(CustomerId),
 FOREIGN KEY(RoomId) REFERENCES Rooms(RoomId),
 FOREIGN KEY(CreatedByUserId) REFERENCES AspNetUsers(Id),
 INDEX Reservations_Room_Status_Start(RoomId,Status,StartTime),
 INDEX Reservations_Customer_Status_Start(CustomerId,Status,StartTime)
) ENGINE=InnoDB;
-- statement
CREATE TABLE IF NOT EXISTS RoomSessions (
 RoomSessionId INT PRIMARY KEY AUTO_INCREMENT,
 CustomerId INT NOT NULL,
 RoomId INT NOT NULL,
 ReservationId INT NULL UNIQUE,
 ActualStartTime VARCHAR(33) NOT NULL CHECK(REGEXP_LIKE(ActualStartTime,'^[0-9]{4}-(0[1-9]|1[0-2])-(0[1-9]|[12][0-9]|3[01])T([01][0-9]|2[0-3]):[0-5][0-9]:[0-5][0-9][.][0-9]{7}[+]00:00$','c')),
 ExpectedEndTime VARCHAR(33) NULL CHECK(ExpectedEndTime IS NULL OR REGEXP_LIKE(ExpectedEndTime,'^[0-9]{4}-(0[1-9]|1[0-2])-(0[1-9]|[12][0-9]|3[01])T([01][0-9]|2[0-3]):[0-5][0-9]:[0-5][0-9][.][0-9]{7}[+]00:00$','c')),
 ActualEndTime VARCHAR(33) NULL CHECK(ActualEndTime IS NULL OR REGEXP_LIKE(ActualEndTime,'^[0-9]{4}-(0[1-9]|1[0-2])-(0[1-9]|[12][0-9]|3[01])T([01][0-9]|2[0-3]):[0-5][0-9]:[0-5][0-9][.][0-9]{7}[+]00:00$','c')),
 HourlyRate BIGINT NOT NULL CHECK(HourlyRate>0),
 RoomCodeSnapshot TEXT NOT NULL CHECK(CHAR_LENGTH(TRIM(RoomCodeSnapshot))>0),
 RoomTypeCodeSnapshot VARCHAR(20) NOT NULL CHECK(RoomTypeCodeSnapshot IN ('STANDARD','VIP')),
 RoomTypeNameSnapshot TEXT NOT NULL CHECK(CHAR_LENGTH(TRIM(RoomTypeNameSnapshot))>0),
 Status VARCHAR(20) NOT NULL CHECK(Status IN ('Active','Completed')),
 ActiveRoomId INT GENERATED ALWAYS AS (CASE WHEN Status='Active' THEN RoomId ELSE NULL END) STORED,
 ActiveCustomerId INT GENERATED ALWAYS AS (CASE WHEN Status='Active' THEN CustomerId ELSE NULL END) STORED,
 UNIQUE RoomSessions_ActiveRoom(ActiveRoomId),
 UNIQUE RoomSessions_ActiveCustomer(ActiveCustomerId),
 CHECK(ExpectedEndTime IS NULL OR ExpectedEndTime>ActualStartTime),
 CHECK(ActualEndTime IS NULL OR ActualEndTime>=ActualStartTime),
 CHECK((Status='Active' AND ActualEndTime IS NULL) OR (Status='Completed' AND ActualEndTime IS NOT NULL)),
 CHECK(ReservationId IS NULL OR ExpectedEndTime IS NOT NULL),
 FOREIGN KEY(CustomerId) REFERENCES Customers(CustomerId),
 FOREIGN KEY(RoomId) REFERENCES Rooms(RoomId),
 FOREIGN KEY(ReservationId) REFERENCES Reservations(ReservationId)
) ENGINE=InnoDB;
-- statement
CREATE TABLE IF NOT EXISTS Services (
 ServiceId INT PRIMARY KEY AUTO_INCREMENT,
 Name VARCHAR(100) NOT NULL CHECK(CHAR_LENGTH(TRIM(Name)) BETWEEN 1 AND 100),
 Category VARCHAR(20) NOT NULL CHECK(Category IN ('Đồ uống','Đồ ăn','Khác')),
 Price BIGINT NOT NULL CHECK(Price>0),
 Description VARCHAR(2000) NULL,
 IsActive TINYINT NOT NULL CHECK(IsActive IN (0,1))
) ENGINE=InnoDB;
-- statement
CREATE TABLE IF NOT EXISTS Orders (
 OrderId INT PRIMARY KEY AUTO_INCREMENT,
 RoomSessionId INT NOT NULL,
 CreatedByUserId VARCHAR(128) NULL,
 Status VARCHAR(20) NOT NULL CHECK(Status IN ('Pending','Completed','Cancelled')),
 CreatedAt VARCHAR(33) NOT NULL CHECK(REGEXP_LIKE(CreatedAt,'^[0-9]{4}-(0[1-9]|1[0-2])-(0[1-9]|[12][0-9]|3[01])T([01][0-9]|2[0-3]):[0-5][0-9]:[0-5][0-9][.][0-9]{7}[+]00:00$','c')),
 FOREIGN KEY(RoomSessionId) REFERENCES RoomSessions(RoomSessionId),
 FOREIGN KEY(CreatedByUserId) REFERENCES AspNetUsers(Id),
 INDEX IX_Orders_Session(RoomSessionId,OrderId)
) ENGINE=InnoDB;
-- statement
CREATE TABLE IF NOT EXISTS OrderItems (
 OrderItemId INT PRIMARY KEY AUTO_INCREMENT,
 OrderId INT NOT NULL,
 ServiceId INT NOT NULL,
 ServiceNameSnapshot VARCHAR(100) NOT NULL CHECK(CHAR_LENGTH(TRIM(ServiceNameSnapshot)) BETWEEN 1 AND 100),
 Quantity INT NOT NULL CHECK(Quantity BETWEEN 1 AND 10),
 UnitPrice BIGINT NOT NULL CHECK(UnitPrice>0),
 UNIQUE(OrderId,ServiceId),
 FOREIGN KEY(OrderId) REFERENCES Orders(OrderId),
 FOREIGN KEY(ServiceId) REFERENCES Services(ServiceId)
) ENGINE=InnoDB;
-- statement
CREATE TABLE IF NOT EXISTS Invoices (
 InvoiceId INT PRIMARY KEY AUTO_INCREMENT,
 RoomSessionId INT NOT NULL UNIQUE,
 InvoiceNumber VARCHAR(50) NOT NULL UNIQUE CHECK(CHAR_LENGTH(TRIM(InvoiceNumber)) BETWEEN 1 AND 50),
 RoomCharge DECIMAL(29,0) NOT NULL CHECK(RoomCharge BETWEEN 0 AND 79228162514264337593543950335),
 ServiceCharge DECIMAL(29,0) NOT NULL CHECK(ServiceCharge BETWEEN 0 AND 79228162514264337593543950335),
 TotalAmount DECIMAL(29,0) NOT NULL CHECK(TotalAmount BETWEEN 0 AND 79228162514264337593543950335),
 PaymentMethod VARCHAR(20) NOT NULL CHECK(PaymentMethod IN ('Cash','BankTransfer')),
 ProcessedByUserId VARCHAR(128) NOT NULL,
 ProcessedByNameSnapshot VARCHAR(100) NOT NULL CHECK(CHAR_LENGTH(TRIM(ProcessedByNameSnapshot)) BETWEEN 1 AND 100),
 PaidAt VARCHAR(33) NOT NULL CHECK(REGEXP_LIKE(PaidAt,'^[0-9]{4}-(0[1-9]|1[0-2])-(0[1-9]|[12][0-9]|3[01])T([01][0-9]|2[0-3]):[0-5][0-9]:[0-5][0-9][.][0-9]{7}[+]00:00$','c')),
 FOREIGN KEY(RoomSessionId) REFERENCES RoomSessions(RoomSessionId),
 FOREIGN KEY(ProcessedByUserId) REFERENCES AspNetUsers(Id),
 INDEX IX_Invoices_PaidAt(PaidAt)
) ENGINE=InnoDB;
