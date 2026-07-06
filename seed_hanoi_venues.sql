-- ============================================================
-- Seed: Hanoi venues for SportsPlatformDB / PicklinkBackend
-- Safe to run multiple times.
--
-- Demo owner password for all accounts below: 123456
-- ============================================================

SET NOCOUNT ON;
SET XACT_ABORT ON;

BEGIN TRY
    BEGIN TRANSACTION;

    -- Older local databases may not have map coordinates yet.
    IF COL_LENGTH(N'VENUE', N'latitude') IS NULL
    BEGIN
        ALTER TABLE [VENUE] ADD [latitude] float NULL;
    END;

    IF COL_LENGTH(N'VENUE', N'longitude') IS NULL
    BEGIN
        ALTER TABLE [VENUE] ADD [longitude] float NULL;
    END;

    IF COL_LENGTH(N'VENUE', N'isOpen') IS NULL
    BEGIN
        ALTER TABLE [VENUE] ADD [isOpen] bit NOT NULL CONSTRAINT [DF_VENUE_isOpen] DEFAULT (1);
    END;

    IF COL_LENGTH(N'VENUE', N'approvalStatus') IS NULL
    BEGIN
        ALTER TABLE [VENUE] ADD [approvalStatus] nvarchar(30) NOT NULL CONSTRAINT [DF_VENUE_approvalStatus] DEFAULT (N'Draft');
    END;

    IF COL_LENGTH(N'VENUE', N'rejectionReason') IS NULL
    BEGIN
        ALTER TABLE [VENUE] ADD [rejectionReason] nvarchar(500) NULL;
    END;

    DECLARE @PasswordHash nvarchar(512) =
        N'v1.100000.AQIDBAUGBwgJCgsMDQ4PEA==.gX0bTflqCjSgps4WRDCI1xtjk/h96ukaUfpnl/iu+QY=';

    DECLARE @Owners table
    (
        Username nvarchar(100) NOT NULL,
        Email nvarchar(255) NOT NULL PRIMARY KEY
    );

    INSERT INTO @Owners (Username, Email)
    VALUES
        (N'hanoi_owner_nguyen', N'hanoi.owner.nguyen@picklink.test'),
        (N'hanoi_owner_tran',   N'hanoi.owner.tran@picklink.test'),
        (N'hanoi_owner_le',     N'hanoi.owner.le@picklink.test');

    UPDATE u
    SET
        u.passwordHash = @PasswordHash,
        u.userType = N'VenueOwner',
        u.city = N'Ha Noi'
    FROM [USER] u
    INNER JOIN @Owners o ON o.Email = u.email;

    INSERT INTO [USER] (username, email, passwordHash, userType, city)
    SELECT o.Username, o.Email, @PasswordHash, N'VenueOwner', N'Ha Noi'
    FROM @Owners o
    WHERE NOT EXISTS (
        SELECT 1
        FROM [USER] u
        WHERE u.email = o.Email
    );

    INSERT INTO [VENUE_OWNER] (userId, specialPermissions)
    SELECT u.userId, N'Hanoi demo venue owner'
    FROM [USER] u
    INNER JOIN @Owners o ON o.Email = u.email
    WHERE NOT EXISTS (
        SELECT 1
        FROM [VENUE_OWNER] vo
        WHERE vo.userId = u.userId
    );

    DECLARE @Venues table
    (
        OwnerEmail nvarchar(255) NOT NULL,
        VenueName nvarchar(200) NOT NULL PRIMARY KEY,
        Address nvarchar(500) NOT NULL,
        OverallRating float NOT NULL,
        OpenTime time NOT NULL,
        CloseTime time NOT NULL,
        PhoneNumber nvarchar(20) NULL,
        Latitude float NOT NULL,
        Longitude float NOT NULL
    );

    INSERT INTO @Venues
        (OwnerEmail, VenueName, Address, OverallRating, OpenTime, CloseTime, PhoneNumber, Latitude, Longitude)
    VALUES
        (N'hanoi.owner.nguyen@picklink.test', N'Hoan Kiem Pickleball Club',
            N'12 Dinh Tien Hoang, Hoan Kiem, Ha Noi', 4.8, '06:00', '22:00', N'0241234001', 21.02897, 105.85227),
        (N'hanoi.owner.nguyen@picklink.test', N'Lake View Sports Center',
            N'3 Le Thai To, Hoan Kiem, Ha Noi', 4.5, '06:00', '21:00', N'0241234002', 21.03101, 105.85497),
        (N'hanoi.owner.nguyen@picklink.test', N'Old Quarter Courts',
            N'35 Hang Be, Hoan Kiem, Ha Noi', 4.2, '07:00', '22:00', N'0241234003', 21.03421, 105.85013),
        (N'hanoi.owner.tran@picklink.test', N'Ba Dinh Grand Arena',
            N'8 Hung Vuong, Ba Dinh, Ha Noi', 4.7, '05:30', '22:00', N'0241234004', 21.04452, 105.83782),
        (N'hanoi.owner.tran@picklink.test', N'West Lake Pickleball Hub',
            N'15 Thanh Nien, Ba Dinh, Ha Noi', 4.6, '06:00', '21:30', N'0241234005', 21.04981, 105.84563),
        (N'hanoi.owner.tran@picklink.test', N'Lang Chu Tich Sport Zone',
            N'1 Ngoc Ha, Ba Dinh, Ha Noi', 4.3, '06:00', '21:00', N'0241234006', 21.03677, 105.83407),
        (N'hanoi.owner.le@picklink.test', N'Dong Da Racket Club',
            N'22 Tay Son, Dong Da, Ha Noi', 4.4, '06:00', '22:00', N'0241234007', 21.02112, 105.84178),
        (N'hanoi.owner.le@picklink.test', N'Van Mieu Courts',
            N'58 Quoc Tu Giam, Dong Da, Ha Noi', 4.6, '06:30', '21:00', N'0241234008', 21.02801, 105.83564),
        (N'hanoi.owner.le@picklink.test', N'Thong Nhat Park Pickleball',
            N'13 Le Duan, Hai Ba Trung, Ha Noi', 4.9, '05:30', '22:00', N'0241234009', 21.01987, 105.85698),
        (N'hanoi.owner.nguyen@picklink.test', N'Truc Bach Outdoor Courts',
            N'47 Nguyen Trung Truc, Ba Dinh, Ha Noi', 4.1, '06:00', '20:30', N'0241234010', 21.04203, 105.84891);

    UPDATE v
    SET
        v.ownerId = vo.ownerId,
        v.address = src.Address,
        v.overallRating = src.OverallRating,
        v.openTime = src.OpenTime,
        v.closeTime = src.CloseTime,
        v.phoneNumber = src.PhoneNumber,
        v.latitude = src.Latitude,
        v.longitude = src.Longitude,
        v.isOpen = 1,
        v.approvalStatus = N'Approved',
        v.rejectionReason = NULL
    FROM [VENUE] v
    INNER JOIN @Venues src ON src.VenueName = v.venueName
    INNER JOIN [USER] u ON u.email = src.OwnerEmail
    INNER JOIN [VENUE_OWNER] vo ON vo.userId = u.userId;

    INSERT INTO [VENUE]
        (ownerId, venueName, address, overallRating, openTime, closeTime, phoneNumber, latitude, longitude, isOpen, approvalStatus, rejectionReason)
    SELECT
        vo.ownerId,
        src.VenueName,
        src.Address,
        src.OverallRating,
        src.OpenTime,
        src.CloseTime,
        src.PhoneNumber,
        src.Latitude,
        src.Longitude,
        1,
        N'Approved',
        NULL
    FROM @Venues src
    INNER JOIN [USER] u ON u.email = src.OwnerEmail
    INNER JOIN [VENUE_OWNER] vo ON vo.userId = u.userId
    WHERE NOT EXISTS (
        SELECT 1
        FROM [VENUE] v
        WHERE v.venueName = src.VenueName
    );

    DECLARE @Courts table
    (
        VenueName nvarchar(200) NOT NULL,
        CourtNumber int NOT NULL,
        SurfaceType nvarchar(100) NOT NULL,
        IsIndoor bit NOT NULL,
        AvailabilityStatus nvarchar(50) NOT NULL,
        PRIMARY KEY (VenueName, CourtNumber)
    );

    INSERT INTO @Courts (VenueName, CourtNumber, SurfaceType, IsIndoor, AvailabilityStatus)
    SELECT
        v.VenueName,
        c.CourtNumber,
        CASE c.CourtNumber
            WHEN 1 THEN N'Acrylic'
            WHEN 2 THEN N'PU'
            ELSE N'Concrete'
        END,
        CASE
            WHEN v.VenueName IN (N'Lake View Sports Center', N'Ba Dinh Grand Arena') THEN 1
            WHEN c.CourtNumber = 3 THEN 1
            ELSE 0
        END,
        N'Available'
    FROM @Venues v
    CROSS JOIN (VALUES (1), (2), (3)) c(CourtNumber);

    UPDATE c
    SET
        c.surfaceType = src.SurfaceType,
        c.isIndoor = src.IsIndoor,
        c.availabilityStatus = src.AvailabilityStatus
    FROM [COURT] c
    INNER JOIN [VENUE] v ON v.venueId = c.venueId
    INNER JOIN @Courts src
        ON src.VenueName = v.venueName
        AND src.CourtNumber = c.courtNumber;

    INSERT INTO [COURT] (venueId, courtNumber, surfaceType, isIndoor, availabilityStatus, hourlyPrice)
    SELECT v.venueId, src.CourtNumber, src.SurfaceType, src.IsIndoor, src.AvailabilityStatus, 0.0
    FROM @Courts src
    INNER JOIN [VENUE] v ON v.venueName = src.VenueName
    WHERE NOT EXISTS (
        SELECT 1
        FROM [COURT] c
        WHERE c.venueId = v.venueId
          AND c.courtNumber = src.CourtNumber
    );

    DECLARE @Amenities table
    (
        VenueName nvarchar(200) NOT NULL,
        AmenityName nvarchar(200) NOT NULL,
        IsFree bit NOT NULL,
        PRIMARY KEY (VenueName, AmenityName)
    );

    INSERT INTO @Amenities (VenueName, AmenityName, IsFree)
    SELECT v.VenueName, a.AmenityName, a.IsFree
    FROM @Venues v
    CROSS JOIN (VALUES
        (N'Parking', 1),
        (N'Changing room', 1),
        (N'Drinking water', 1),
        (N'Equipment rental', 0)
    ) a(AmenityName, IsFree);

    UPDATE a
    SET a.isFree = src.IsFree
    FROM [AMENITY] a
    INNER JOIN [VENUE] v ON v.venueId = a.venueId
    INNER JOIN @Amenities src
        ON src.VenueName = v.venueName
        AND src.AmenityName = a.amenityName;

    INSERT INTO [AMENITY] (venueId, amenityName, isFree)
    SELECT v.venueId, src.AmenityName, src.IsFree
    FROM @Amenities src
    INNER JOIN [VENUE] v ON v.venueName = src.VenueName
    WHERE NOT EXISTS (
        SELECT 1
        FROM [AMENITY] a
        WHERE a.venueId = v.venueId
          AND a.amenityName = src.AmenityName
    );

    DECLARE @Rules table
    (
        VenueName nvarchar(200) NOT NULL,
        RuleType nvarchar(100) NOT NULL,
        RuleContent varchar(max) NOT NULL,
        PRIMARY KEY (VenueName, RuleType)
    );

    INSERT INTO @Rules (VenueName, RuleType, RuleContent)
    SELECT v.VenueName, r.RuleType, r.RuleContent
    FROM @Venues v
    CROSS JOIN (VALUES
        (N'Cancellation', 'Cancel at least 2 hours before start time.'),
        (N'CheckIn', 'Players should check in 10 minutes before booking time.'),
        (N'PeakHours', 'Peak hours are 17:00 to 21:00 on weekdays.')
    ) r(RuleType, RuleContent);

    UPDATE br
    SET br.ruleContent = src.RuleContent
    FROM [BOOKING_RULES] br
    INNER JOIN [VENUE] v ON v.venueId = br.venueId
    INNER JOIN @Rules src
        ON src.VenueName = v.venueName
        AND src.RuleType = br.ruleType;

    INSERT INTO [BOOKING_RULES] (venueId, ruleType, ruleContent)
    SELECT v.venueId, src.RuleType, src.RuleContent
    FROM @Rules src
    INNER JOIN [VENUE] v ON v.venueName = src.VenueName
    WHERE NOT EXISTS (
        SELECT 1
        FROM [BOOKING_RULES] br
        WHERE br.venueId = v.venueId
          AND br.ruleType = src.RuleType
    );

    DECLARE @ListingPaidFrom datetime = DATEADD(day, -1, GETUTCDATE());
    DECLARE @ListingPaidUntil datetime = DATEADD(year, 1, GETUTCDATE());
    DECLARE @PricePerCourtPerMonth decimal(18, 2) = 50000.00;
    DECLARE @DemoListings table
    (
        VenueId int NOT NULL PRIMARY KEY,
        ActiveCourtCount int NOT NULL
    );

    INSERT INTO @DemoListings (VenueId, ActiveCourtCount)
    SELECT
        v.venueId,
        COUNT(c.courtId)
    FROM @Venues src
    INNER JOIN [VENUE] v ON v.venueName = src.VenueName
    INNER JOIN [COURT] c
        ON c.venueId = v.venueId
        AND c.availabilityStatus <> N'Inactive'
    GROUP BY v.venueId;

    UPDATE payment
    SET
        payment.months = 12,
        payment.activeCourtCount = listing.ActiveCourtCount,
        payment.pricePerCourtPerMonth = @PricePerCourtPerMonth,
        payment.amount = listing.ActiveCourtCount * 12 * @PricePerCourtPerMonth,
        payment.status = N'Confirmed',
        payment.receiptImageUrl = COALESCE(payment.receiptImageUrl, N'/uploads/payment-receipts/demo-listing-fee.png'),
        payment.rejectionReason = NULL,
        payment.submittedAt = COALESCE(payment.submittedAt, @ListingPaidFrom),
        payment.reviewedAt = COALESCE(payment.reviewedAt, GETUTCDATE()),
        payment.paidFrom = @ListingPaidFrom,
        payment.paidUntil = @ListingPaidUntil
    FROM [VENUE_LISTING_PAYMENT] payment
    INNER JOIN @DemoListings listing ON listing.VenueId = payment.venueId
    WHERE payment.status = N'Confirmed';

    INSERT INTO [VENUE_LISTING_PAYMENT]
        (venueId, months, activeCourtCount, pricePerCourtPerMonth, amount, status, receiptImageUrl, rejectionReason, submittedAt, reviewedAt, reviewedByUserId, paidFrom, paidUntil)
    SELECT
        listing.VenueId,
        12,
        listing.ActiveCourtCount,
        @PricePerCourtPerMonth,
        listing.ActiveCourtCount * 12 * @PricePerCourtPerMonth,
        N'Confirmed',
        N'/uploads/payment-receipts/demo-listing-fee.png',
        NULL,
        @ListingPaidFrom,
        GETUTCDATE(),
        NULL,
        @ListingPaidFrom,
        @ListingPaidUntil
    FROM @DemoListings listing
    WHERE NOT EXISTS (
        SELECT 1
        FROM [VENUE_LISTING_PAYMENT] payment
        WHERE payment.venueId = listing.VenueId
          AND payment.status = N'Confirmed'
    );

    COMMIT TRANSACTION;

    PRINT 'Seeded Hanoi venues successfully.';
    PRINT 'Owner demo accounts: hanoi.owner.nguyen@picklink.test, hanoi.owner.tran@picklink.test, hanoi.owner.le@picklink.test';
    PRINT 'Password: 123456';
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0
    BEGIN
        ROLLBACK TRANSACTION;
    END;

    THROW;
END CATCH;
