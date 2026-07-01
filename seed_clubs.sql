-- ============================================================
-- Seed: Demo Clubs (Social Groups) for PicklinkBackend
-- Safe to run multiple times (idempotent upsert pattern).
--
-- Creates:
--   - 4 demo users (club owners & members)
--   - Player records for each user
--   - 2 Social Groups (clubs)
--   - Group members, images, and posts
--
-- Demo password for all accounts: 123456
-- ============================================================

SET NOCOUNT ON;
SET XACT_ABORT ON;

BEGIN TRY
    BEGIN TRANSACTION;

    DECLARE @PasswordHash nvarchar(512) =
        N'v1.100000.AQIDBAUGBwgJCgsMDQ4PEA==.gX0bTflqCjSgps4WRDCI1xtjk/h96ukaUfpnl/iu+QY=';

    -- ──────────────────────────────
    -- 1. Users
    -- ──────────────────────────────

    DECLARE @ClubUsers table
    (
        Username nvarchar(100) NOT NULL,
        Email nvarchar(255) NOT NULL PRIMARY KEY,
        ProfileImageUrl nvarchar(500) NULL,
        City nvarchar(200) NULL,
        Commune nvarchar(200) NULL
    );

    INSERT INTO @ClubUsers (Username, Email, ProfileImageUrl, City, Commune)
    VALUES
        (N'nguyen_van_an',   N'nguyenvanan@picklink.test',   N'https://i.pravatar.cc/160?img=13', N'Ha Noi', N'Cau Giay'),
        (N'linh_nguyen',     N'linhnguyen@picklink.test',    N'https://i.pravatar.cc/160?img=47', N'Ha Noi', N'Dong Da'),
        (N'tuan_tran',       N'tuantran@picklink.test',      N'https://i.pravatar.cc/160?img=12', N'Ha Noi', N'Ba Dinh'),
        (N'minh_pham',       N'minhpham@picklink.test',      N'https://i.pravatar.cc/160?img=33', N'Ha Noi', N'Hoan Kiem'),
        (N'hoang_long',      N'hoanglong@picklink.test',     N'https://i.pravatar.cc/160?img=60', N'Ho Chi Minh', N'District 1'),
        (N'mai_phuong',      N'maiphuong@picklink.test',     N'https://i.pravatar.cc/160?img=49', N'Da Nang', N'Hai Chau'),
        (N'quoc_anh',        N'quocanh@picklink.test',       N'https://i.pravatar.cc/160?img=8', N'Ha Noi', N'Cau Giay');

    -- Update existing users
    UPDATE u
    SET
        u.passwordHash = @PasswordHash,
        u.userType = N'Player',
        u.profileImageUrl = cu.ProfileImageUrl,
        u.city = cu.City,
        u.commune = cu.Commune
    FROM [USER] u
    INNER JOIN @ClubUsers cu ON cu.Email = u.email;

    -- Insert new users
    INSERT INTO [USER] (username, email, passwordHash, userType, profileImageUrl, city, commune)
    SELECT cu.Username, cu.Email, @PasswordHash, N'Player', cu.ProfileImageUrl, cu.City, cu.Commune
    FROM @ClubUsers cu
    WHERE NOT EXISTS (
        SELECT 1 FROM [USER] u WHERE u.email = cu.Email
    );

    -- ──────────────────────────────
    -- 2. Players (one per user)
    -- ──────────────────────────────

    INSERT INTO [PLAYER] (userId, prestige, skillLevel)
    SELECT u.userId, 100, 3.5
    FROM [USER] u
    INNER JOIN @ClubUsers cu ON cu.Email = u.email
    WHERE NOT EXISTS (
        SELECT 1 FROM [PLAYER] p WHERE p.userId = u.userId
    );

    -- ──────────────────────────────
    -- 3. Social Groups (Clubs)
    -- ──────────────────────────────

    DECLARE @OwnerUserId_An INT;
    SELECT @OwnerUserId_An = u.userId FROM [USER] u WHERE u.email = N'nguyenvanan@picklink.test';

    DECLARE @OwnerPlayerId_An INT;
    SELECT @OwnerPlayerId_An = p.playerId FROM [PLAYER] p WHERE p.userId = @OwnerUserId_An;

    -- Club 1: Hanoi Elite Pickleball Club
    IF NOT EXISTS (SELECT 1 FROM [SOCIAL_GROUP] WHERE groupName = N'Hanoi Elite Pickleball Club')
    BEGIN
        INSERT INTO [SOCIAL_GROUP]
            (ownerId, groupName, description, groupType, coverImageUrl, rules, overallRating, ratingCount, createdAt)
        VALUES
            (@OwnerPlayerId_An,
             N'Hanoi Elite Pickleball Club',
             N'Cộng đồng pickleball năng động tại Hà Nội, phù hợp người chơi muốn tập luyện đều đặn, tìm bạn đánh đôi và tham gia giải nội bộ hàng tháng. CLB duy trì lịch chơi cố định 5 buổi mỗi tuần, có nhóm trình độ rõ ràng và đội ngũ điều phối giúp người mới dễ hòa nhập.',
             N'Public',
             N'https://images.unsplash.com/photo-1626245465352-87ff55a6d0ab?q=80&w=1800&auto=format&fit=crop',
             N'Đến sân trước giờ chơi 10 phút|Mang giày đế non-marking|Xác nhận hủy kèo trước 6 giờ|Giữ tinh thần fair-play',
             4.8,
             128,
             '2022-03-12T10:00:00');
    END;

    -- Club 2: Tay Ho Pickle Squad
    DECLARE @OwnerUserId_Tuan INT;
    SELECT @OwnerUserId_Tuan = u.userId FROM [USER] u WHERE u.email = N'tuantran@picklink.test';

    DECLARE @OwnerPlayerId_Tuan INT;
    SELECT @OwnerPlayerId_Tuan = p.playerId FROM [PLAYER] p WHERE p.userId = @OwnerUserId_Tuan;

    IF NOT EXISTS (SELECT 1 FROM [SOCIAL_GROUP] WHERE groupName = N'Tay Ho Pickle Squad')
    BEGIN
        INSERT INTO [SOCIAL_GROUP]
            (ownerId, groupName, description, groupType, coverImageUrl, rules, overallRating, ratingCount, createdAt)
        VALUES
            (@OwnerPlayerId_Tuan,
             N'Tay Ho Pickle Squad',
             N'Nhóm chơi pickleball khu vực Tây Hồ. Giao lưu vui vẻ, không áp lực. Phù hợp mọi trình độ từ beginner đến advanced.',
             N'Public',
             N'https://images.unsplash.com/photo-1599474924187-334a4ae5bd3c?q=80&w=1800&auto=format&fit=crop',
             N'Tôn trọng lịch chơi của nhóm|Thông báo vắng trước 2 giờ|Giữ gìn dụng cụ chung',
             4.5,
             64,
             '2023-06-15T08:00:00');
    END;

    -- Club 3: Saigon Spinners Club
    DECLARE @OwnerUserId_Long INT;
    SELECT @OwnerUserId_Long = u.userId FROM [USER] u WHERE u.email = N'hoanglong@picklink.test';

    DECLARE @OwnerPlayerId_Long INT;
    SELECT @OwnerPlayerId_Long = p.playerId FROM [PLAYER] p WHERE p.userId = @OwnerUserId_Long;

    IF NOT EXISTS (SELECT 1 FROM [SOCIAL_GROUP] WHERE groupName = N'Saigon Spinners Club')
    BEGIN
        INSERT INTO [SOCIAL_GROUP]
            (ownerId, groupName, description, groupType, coverImageUrl, rules, overallRating, ratingCount, createdAt)
        VALUES
            (@OwnerPlayerId_Long,
             N'Saigon Spinners Club',
             N'CLB Pickleball tại TP.HCM dành cho các tay vợt đam mê lối chơi tấn công, xoáy và tốc độ cao. Sân chơi quy tụ nhiều người chơi trình độ trung bình khá trở lên, thường xuyên tổ chức giao hữu cọ xát cuối tuần.',
             N'Public',
             N'https://images.unsplash.com/photo-1595435934249-5df7ed86e1c0?q=80&w=1800&auto=format&fit=crop',
             N'Đánh bóng đúng luật|Đăng ký giờ chơi qua group chat|Chia tiền sân đều|Giữ thái độ hòa nhã',
             4.7,
             42,
             '2024-01-20T14:30:00');
    END;

    -- Club 4: Da Nang Ocean Pickleball
    DECLARE @OwnerUserId_Phuong INT;
    SELECT @OwnerUserId_Phuong = u.userId FROM [USER] u WHERE u.email = N'maiphuong@picklink.test';

    DECLARE @OwnerPlayerId_Phuong INT;
    SELECT @OwnerPlayerId_Phuong = p.playerId FROM [PLAYER] p WHERE p.userId = @OwnerUserId_Phuong;

    IF NOT EXISTS (SELECT 1 FROM [SOCIAL_GROUP] WHERE groupName = N'Da Nang Ocean Pickleball')
    BEGIN
        INSERT INTO [SOCIAL_GROUP]
            (ownerId, groupName, description, groupType, coverImageUrl, rules, overallRating, ratingCount, createdAt)
        VALUES
            (@OwnerPlayerId_Phuong,
             N'Da Nang Ocean Pickleball',
             N'Nơi kết nối đam mê Pickleball tại thành phố biển Đà Nẵng xinh đẹp. CLB đón chào cả người bản địa lẫn du khách đến giao lưu. Môi trường thân thiện, vừa chơi thể thao vừa tận hưởng không khí biển mát lành.',
             N'Public',
             N'https://images.unsplash.com/photo-1507525428034-b723cf961d3e?q=80&w=1800&auto=format&fit=crop',
             N'Tôn trọng bạn chơi|Không mang đồ ăn lên thảm đấu|Nghỉ ngơi uống nước đúng nơi quy định',
             4.6,
             28,
             '2024-03-05T09:15:00');
    END;

    -- Club 5: Cau Giay Picklers
    DECLARE @OwnerUserId_QuocAnh INT;
    SELECT @OwnerUserId_QuocAnh = u.userId FROM [USER] u WHERE u.email = N'quocanh@picklink.test';

    DECLARE @OwnerPlayerId_QuocAnh INT;
    SELECT @OwnerPlayerId_QuocAnh = p.playerId FROM [PLAYER] p WHERE p.userId = @OwnerUserId_QuocAnh;

    IF NOT EXISTS (SELECT 1 FROM [SOCIAL_GROUP] WHERE groupName = N'Cau Giay Picklers')
    BEGIN
        INSERT INTO [SOCIAL_GROUP]
            (ownerId, groupName, description, groupType, coverImageUrl, rules, overallRating, ratingCount, createdAt)
        VALUES
            (@OwnerPlayerId_QuocAnh,
             N'Cau Giay Picklers',
             N'Cộng đồng Pickleball quận Cầu Giấy sôi động. Chuyên tổ chức các buổi giao lưu tối thứ 3, 5, 7 từ 18:00 đến 21:00. Rất hoan nghênh các bạn newbie đến để được hướng dẫn và tập luyện cơ bản.',
             N'Public',
             N'https://images.unsplash.com/photo-1626245465352-87ff55a6d0ab?q=80&w=1800&auto=format&fit=crop',
             N'Ra sân đúng giờ|Hỗ trợ nhặt bóng|Chia sẻ kinh nghiệm với người mới',
             4.9,
             85,
             '2023-11-10T17:00:00');
    END;

    -- ──────────────────────────────
    -- 4. Group Members
    -- ──────────────────────────────

    DECLARE @GroupId_HEPC INT;
    SELECT @GroupId_HEPC = groupId FROM [SOCIAL_GROUP] WHERE groupName = N'Hanoi Elite Pickleball Club';

    DECLARE @GroupId_THPS INT;
    SELECT @GroupId_THPS = groupId FROM [SOCIAL_GROUP] WHERE groupName = N'Tay Ho Pickle Squad';

    DECLARE @GroupId_SSC INT;
    SELECT @GroupId_SSC = groupId FROM [SOCIAL_GROUP] WHERE groupName = N'Saigon Spinners Club';

    DECLARE @GroupId_DOP INT;
    SELECT @GroupId_DOP = groupId FROM [SOCIAL_GROUP] WHERE groupName = N'Da Nang Ocean Pickleball';

    DECLARE @GroupId_CGP INT;
    SELECT @GroupId_CGP = groupId FROM [SOCIAL_GROUP] WHERE groupName = N'Cau Giay Picklers';

    -- Members for HEPC (all 4 users)
    DECLARE @MemberData table (GroupId INT, Email nvarchar(255), Role nvarchar(50));
    INSERT INTO @MemberData VALUES
        (@GroupId_HEPC, N'nguyenvanan@picklink.test', N'Owner'),
        (@GroupId_HEPC, N'linhnguyen@picklink.test',  N'Admin'),
        (@GroupId_HEPC, N'tuantran@picklink.test',    N'Member'),
        (@GroupId_HEPC, N'minhpham@picklink.test',    N'Member'),
        (@GroupId_THPS, N'tuantran@picklink.test',    N'Owner'),
        (@GroupId_THPS, N'linhnguyen@picklink.test',  N'Member'),
        (@GroupId_THPS, N'minhpham@picklink.test',    N'Member'),
        (@GroupId_SSC,  N'hoanglong@picklink.test',   N'Owner'),
        (@GroupId_SSC,  N'nguyenvanan@picklink.test', N'Member'),
        (@GroupId_DOP,  N'maiphuong@picklink.test',   N'Owner'),
        (@GroupId_DOP,  N'linhnguyen@picklink.test',  N'Member'),
        (@GroupId_CGP,  N'quocanh@picklink.test',     N'Owner'),
        (@GroupId_CGP,  N'tuantran@picklink.test',    N'Member'),
        (@GroupId_CGP,  N'minhpham@picklink.test',    N'Member');

    INSERT INTO [GROUP_MEMBER] (groupId, userId, role, status, joinedAt)
    SELECT md.GroupId, u.userId, md.Role, N'Accepted', GETUTCDATE()
    FROM @MemberData md
    INNER JOIN [USER] u ON u.email = md.Email
    WHERE NOT EXISTS (
        SELECT 1 FROM [GROUP_MEMBER] gm
        WHERE gm.groupId = md.GroupId AND gm.userId = u.userId
    );

    -- ──────────────────────────────
    -- 5. Group Images
    -- ──────────────────────────────

    DECLARE @ImageData table (GroupId INT, ImageUrl nvarchar(500), Caption nvarchar(200), SortOrder INT);
    INSERT INTO @ImageData VALUES
        (@GroupId_HEPC, N'https://images.unsplash.com/photo-1599474924187-334a4ae5bd3c?q=80&w=800&auto=format&fit=crop', N'Open play cuối tuần', 0),
        (@GroupId_HEPC, N'https://images.unsplash.com/photo-1629904853716-f0bc54eea481?q=80&w=800&auto=format&fit=crop', N'Giải đấu nội bộ tháng 5', 1),
        (@GroupId_HEPC, N'https://images.unsplash.com/photo-1642501518638-6f9d6e40496d?q=80&w=800&auto=format&fit=crop', N'Lớp kỹ thuật tuần này', 2),
        (@GroupId_THPS, N'https://images.unsplash.com/photo-1626245465352-87ff55a6d0ab?q=80&w=800&auto=format&fit=crop', N'Sân chơi Tây Hồ', 0),
        (@GroupId_THPS, N'https://images.unsplash.com/photo-1599474924187-334a4ae5bd3c?q=80&w=800&auto=format&fit=crop', N'Buổi giao lưu', 1),
        (@GroupId_SSC,  N'https://images.unsplash.com/photo-1595435934249-5df7ed86e1c0?q=80&w=800&auto=format&fit=crop', N'Sân Saigon Spinners', 0),
        (@GroupId_DOP,  N'https://images.unsplash.com/photo-1507525428034-b723cf961d3e?q=80&w=800&auto=format&fit=crop', N'Giao lưu ven biển', 0),
        (@GroupId_CGP,  N'https://images.unsplash.com/photo-1626245465352-87ff55a6d0ab?q=80&w=800&auto=format&fit=crop', N'Sân Cầu Giấy', 0);

    INSERT INTO [GROUP_IMAGE] (groupId, imageUrl, caption, sortOrder, createdAt)
    SELECT id.GroupId, id.ImageUrl, id.Caption, id.SortOrder, GETUTCDATE()
    FROM @ImageData id
    WHERE NOT EXISTS (
        SELECT 1 FROM [GROUP_IMAGE] gi
        WHERE gi.groupId = id.GroupId AND gi.sortOrder = id.SortOrder
    );

    -- ──────────────────────────────
    -- 6. Posts
    -- ──────────────────────────────

    IF NOT EXISTS (SELECT 1 FROM [POST] WHERE groupId = @GroupId_HEPC)
    BEGIN
        INSERT INTO [POST] (groupId, authorId, content, postType, visibility, createdAt, updatedAt)
        VALUES
            (@GroupId_HEPC, @OwnerUserId_An,
             N'Kết quả ladder tuần này: Bảng A có nhiều trận kéo dài tới set quyết định. CLB sẽ cập nhật điểm xếp hạng vào tối nay.',
             N'Post', N'Group', DATEADD(HOUR, -2, GETUTCDATE()), DATEADD(HOUR, -2, GETUTCDATE())),
            (@GroupId_HEPC, @OwnerUserId_An,
             N'Mở đăng ký lớp beginner: Lớp nhập môn khai giảng tối thứ 5, phù hợp người mới mua vợt hoặc muốn học luật chơi căn bản.',
             N'Post', N'Group', DATEADD(DAY, -1, GETUTCDATE()), DATEADD(DAY, -1, GETUTCDATE()));
    END;

    IF NOT EXISTS (SELECT 1 FROM [POST] WHERE groupId = @GroupId_SSC)
    BEGIN
        INSERT INTO [POST] (groupId, authorId, content, postType, visibility, createdAt, updatedAt)
        VALUES
            (@GroupId_SSC, @OwnerUserId_Long,
             N'Thành viên mới lưu ý đăng ký tham gia buổi cọ xát thứ 7 tuần này nhé. Số lượng giới hạn 16 người.',
             N'Post', N'Group', DATEADD(HOUR, -4, GETUTCDATE()), DATEADD(HOUR, -4, GETUTCDATE()));
    END;

    IF NOT EXISTS (SELECT 1 FROM [POST] WHERE groupId = @GroupId_DOP)
    BEGIN
        INSERT INTO [POST] (groupId, authorId, content, postType, visibility, createdAt, updatedAt)
        VALUES
            (@GroupId_DOP, @OwnerUserId_Phuong,
             N'Chào mừng các bạn du khách nước ngoài đến giao lưu hôm qua. Các trận đấu đôi nam nữ diễn ra cực kỳ kịch tính!',
             N'Post', N'Group', DATEADD(DAY, -2, GETUTCDATE()), DATEADD(DAY, -2, GETUTCDATE()));
    END;

    IF NOT EXISTS (SELECT 1 FROM [POST] WHERE groupId = @GroupId_CGP)
    BEGIN
        INSERT INTO [POST] (groupId, authorId, content, postType, visibility, createdAt, updatedAt)
        VALUES
            (@GroupId_CGP, @OwnerUserId_QuocAnh,
             N'Hôm nay lớp beginner vẫn sinh hoạt bình thường lúc 18h. Các bạn nhớ mang theo nước uống cá nhân nhé.',
             N'Post', N'Group', DATEADD(HOUR, -1, GETUTCDATE()), DATEADD(HOUR, -1, GETUTCDATE()));
    END;

    COMMIT TRANSACTION;

    PRINT 'Seeded demo clubs successfully.';
    PRINT 'Club owners: nguyenvanan@picklink.test, tuantran@picklink.test, hoanglong@picklink.test, maiphuong@picklink.test, quocanh@picklink.test / 123456';
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0
    BEGIN
        ROLLBACK TRANSACTION;
    END;

    THROW;
END CATCH;
