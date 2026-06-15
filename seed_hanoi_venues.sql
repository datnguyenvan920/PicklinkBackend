-- ============================================================
-- Seed: Hanoi Sports Venues (center ≈ 21.0285, 105.8542)
-- ============================================================

SET IDENTITY_INSERT [USER] OFF;

-- ── 1. Seed owner accounts ─────────────────────────────────
INSERT INTO [USER] (username, email, passwordHash, userType, city)
VALUES
  ('owner_nguyen',  'owner.nguyen@picklinktest.com',  '$2a$11$placeholder_hash_owner1', 'VenueOwner', 'Ha Noi'),
  ('owner_tran',    'owner.tran@picklinktest.com',    '$2a$11$placeholder_hash_owner2', 'VenueOwner', 'Ha Noi'),
  ('owner_le',      'owner.le@picklinktest.com',      '$2a$11$placeholder_hash_owner3', 'VenueOwner', 'Ha Noi');

-- ── 2. Seed venue owner profiles ──────────────────────────
INSERT INTO [VENUE_OWNER] (userId)
SELECT userId FROM [USER] WHERE email IN (
  'owner.nguyen@picklinktest.com',
  'owner.tran@picklinktest.com',
  'owner.le@picklinktest.com'
);

-- ── 3. Seed venues around central Hanoi ───────────────────
--   Coordinates are all within ~2 km of Hoan Kiem Lake (21.0285, 105.8542)
DECLARE @owner1 INT = (SELECT ownerId FROM VENUE_OWNER WHERE userId = (SELECT userId FROM [USER] WHERE email = 'owner.nguyen@picklinktest.com'));
DECLARE @owner2 INT = (SELECT ownerId FROM VENUE_OWNER WHERE userId = (SELECT userId FROM [USER] WHERE email = 'owner.tran@picklinktest.com'));
DECLARE @owner3 INT = (SELECT ownerId FROM VENUE_OWNER WHERE userId = (SELECT userId FROM [USER] WHERE email = 'owner.le@picklinktest.com'));

INSERT INTO [VENUE] (ownerId, venueName, address, overallRating, openTime, closeTime, phoneNumber, latitude, longitude)
VALUES
  -- Hoàn Kiếm area
  (@owner1, N'Hoan Kiem Pickleball Club',          N'12 Đinh Tiên Hoàng, Hoàn Kiếm, Hà Nội',     4.8, '06:00', '22:00', '0241234001', 21.02897, 105.85227),
  (@owner1, N'Lake View Sports Center',             N'3 Lê Thái Tổ, Hoàn Kiếm, Hà Nội',           4.5, '06:00', '21:00', '0241234002', 21.03101, 105.85497),
  (@owner1, N'Old Quarter Courts',                  N'35 Hàng Bè, Hoàn Kiếm, Hà Nội',             4.2, '07:00', '22:00', '0241234003', 21.03421, 105.85013),

  -- Ba Đình area
  (@owner2, N'Ba Dinh Grand Arena',                 N'8 Hùng Vương, Ba Đình, Hà Nội',             4.7, '05:30', '22:00', '0241234004', 21.04452, 105.83782),
  (@owner2, N'West Lake Pickleball Hub',            N'15 Thanh Niên, Ba Đình, Hà Nội',             4.6, '06:00', '21:30', '0241234005', 21.04981, 105.84563),
  (@owner2, N'Lăng Chủ Tịch Sport Zone',           N'1 Ngọc Hà, Ba Đình, Hà Nội',                4.3, '06:00', '21:00', '0241234006', 21.03677, 105.83407),

  -- Đống Đa area
  (@owner3, N'Dong Da Racket Club',                 N'22 Tây Sơn, Đống Đa, Hà Nội',               4.4, '06:00', '22:00', '0241234007', 21.02112, 105.84178),
  (@owner3, N'Van Mieu Courts',                     N'58 Quốc Tử Giám, Đống Đa, Hà Nội',          4.6, '06:30', '21:00', '0241234008', 21.02801, 105.83564),

  -- Hai Bà Trưng area  
  (@owner3, N'Thong Nhat Park Pickleball',          N'13 Lê Duẩn, Hai Bà Trưng, Hà Nội',          4.9, '05:30', '22:00', '0241234009', 21.01987, 105.85698),
  (@owner1, N'Truc Bach Outdoor Courts',            N'47 Nguyễn Trung Trực, Ba Đình, Hà Nội',     4.1, '06:00', '20:30', '0241234010', 21.04203, 105.84891);

PRINT 'Seeded 3 owners and 10 venues around central Hanoi successfully.';
