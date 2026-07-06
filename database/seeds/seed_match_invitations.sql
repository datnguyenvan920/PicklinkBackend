-- Demo data for /opponents, /opponents/create and /my-matches.
-- All demo player accounts use password: 123456
-- Safe to rerun while the demo matches do not have bookings.
-- Run from repository root with: sqlcmd -S localhost -d SportsPlatformDB -E -C -b -f 65001 -i database\seeds\seed_match_invitations.sql

SET NOCOUNT ON;
SET XACT_ABORT ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET ANSI_PADDING ON;
SET ANSI_WARNINGS ON;
SET ARITHABORT ON;
SET CONCAT_NULL_YIELDS_NULL ON;
SET NUMERIC_ROUNDABORT OFF;

BEGIN TRY
    BEGIN TRANSACTION;

    DECLARE @PasswordHash nvarchar(512) =
        N'v1.100000.AQIDBAUGBwgJCgsMDQ4PEA==.gX0bTflqCjSgps4WRDCI1xtjk/h96ukaUfpnl/iu+QY=';

    DECLARE @DemoPlayers table
    (
        Seq int NOT NULL PRIMARY KEY,
        Username nvarchar(100) NOT NULL,
        Email nvarchar(255) NOT NULL,
        SkillLevel float NOT NULL,
        Prestige int NOT NULL
    );

    INSERT INTO @DemoPlayers (Seq, Username, Email, SkillLevel, Prestige)
    VALUES
        (1, N'Demo Host An',    N'match.demo1@picklink.test', 2.5, 130),
        (2, N'Demo Player Bình', N'match.demo2@picklink.test', 3.0, 160),
        (3, N'Demo Player Chi',  N'match.demo3@picklink.test', 3.5, 190),
        (4, N'Demo Player Dũng', N'match.demo4@picklink.test', 4.0, 220),
        (5, N'Demo Player Hà',   N'match.demo5@picklink.test', 2.8, 145),
        (6, N'Demo Player Khoa', N'match.demo6@picklink.test', 3.2, 175),
        (7, N'Demo Player Linh', N'match.demo7@picklink.test', 3.8, 205),
        (8, N'Demo Player Minh', N'match.demo8@picklink.test', 4.2, 235);

    UPDATE u
    SET
        u.username = source.Username,
        u.passwordHash = @PasswordHash,
        u.userType = N'Player',
        u.city = N'Hà Nội',
        u.commune = N'Cầu Giấy'
    FROM [USER] u
    INNER JOIN @DemoPlayers source ON source.Email = u.email;

    INSERT INTO [USER] (username, email, passwordHash, userType, city, commune)
    SELECT source.Username, source.Email, @PasswordHash, N'Player', N'Hà Nội', N'Cầu Giấy'
    FROM @DemoPlayers source
    WHERE NOT EXISTS (SELECT 1 FROM [USER] u WHERE u.email = source.Email);

    UPDATE p
    SET
        p.skillLevel = source.SkillLevel,
        p.prestige = source.Prestige,
        p.playerSubType = N'Competitive',
        p.playFrequency = N'3-4 buổi/tuần',
        p.preferredTimeSlot = N'Sáng sớm hoặc buổi tối',
        p.bio = N'Tài khoản demo dùng để kiểm thử ghép trận và lời mời.'
    FROM [PLAYER] p
    INNER JOIN [USER] u ON u.userId = p.userId
    INNER JOIN @DemoPlayers source ON source.Email = u.email;

    INSERT INTO [PLAYER] (userId, prestige, skillLevel, playerSubType, playFrequency, preferredTimeSlot, bio)
    SELECT
        u.userId,
        source.Prestige,
        source.SkillLevel,
        N'Competitive',
        N'3-4 buổi/tuần',
        N'Sáng sớm hoặc buổi tối',
        N'Tài khoản demo dùng để kiểm thử ghép trận và lời mời.'
    FROM @DemoPlayers source
    INNER JOIN [USER] u ON u.email = source.Email
    WHERE NOT EXISTS (SELECT 1 FROM [PLAYER] p WHERE p.userId = u.userId);

    IF EXISTS (
        SELECT 1
        FROM [MATCH] m
        INNER JOIN [BOOKING] b ON b.matchId = m.matchId
        WHERE m.title LIKE N'DEMO MATCH %'
    )
        THROW 51000, 'Không thể seed lại vì một trận demo đã có booking. Hãy hủy/xóa booking demo trước.', 1;

    DECLARE @OldMatchIds table (MatchId int NOT NULL PRIMARY KEY);
    INSERT INTO @OldMatchIds (MatchId)
    SELECT matchId FROM [MATCH] WHERE title LIKE N'DEMO MATCH %';

    DELETE nl
    FROM [NOTIFICATION_LOG] nl
    WHERE LEFT(nl.message, 12) = N'[DEMO MATCH]';

    DELETE cp
    FROM [CONVERSATION_PARTICIPANT] cp
    INNER JOIN [CONVERSATION] c ON c.conversationId = cp.conversationId
    INNER JOIN @OldMatchIds oldMatch ON oldMatch.MatchId = c.matchId;

    DELETE msg
    FROM [MESSAGE] msg
    INNER JOIN [CONVERSATION] c ON c.conversationId = msg.conversationId
    INNER JOIN @OldMatchIds oldMatch ON oldMatch.MatchId = c.matchId;

    DELETE c
    FROM [CONVERSATION] c
    INNER JOIN @OldMatchIds oldMatch ON oldMatch.MatchId = c.matchId;

    DELETE slot
    FROM [MATCH_AVAILABILITY_SLOT] slot
    INNER JOIN @OldMatchIds oldMatch ON oldMatch.MatchId = slot.matchId;

    DELETE participant
    FROM [MATCH_PARTICIPANT] participant
    INNER JOIN @OldMatchIds oldMatch ON oldMatch.MatchId = participant.matchId;

    DELETE review
    FROM [MATCH_PLAYER_REVIEW] review
    INNER JOIN @OldMatchIds oldMatch ON oldMatch.MatchId = review.matchId;

    DELETE checkin
    FROM [MATCH_CHECKIN] checkin
    INNER JOIN @OldMatchIds oldMatch ON oldMatch.MatchId = checkin.matchId;

    DELETE m
    FROM [MATCH] m
    INNER JOIN @OldMatchIds oldMatch ON oldMatch.MatchId = m.matchId;

    DECLARE @DemoMatches table
    (
        Seq int NOT NULL PRIMARY KEY,
        HostSeq int NOT NULL,
        MatchType nvarchar(20) NOT NULL,
        RequiredPlayerCount int NOT NULL,
        Title nvarchar(200) NOT NULL,
        Note nvarchar(1000) NOT NULL,
        VenueIds nvarchar(500) NOT NULL,
        Latitude float NOT NULL,
        Longitude float NOT NULL
    );

    INSERT INTO @DemoMatches
        (Seq, HostSeq, MatchType, RequiredPlayerCount, Title, Note, VenueIds, Latitude, Longitude)
    VALUES
        (1, 1, N'1vs1', 2, N'DEMO MATCH 01 - Đánh đơn sáng cuối tuần',      N'Tìm một bạn đánh đơn, ưu tiên vui vẻ và đúng giờ.',               N'1,2',   21.0285, 105.8048),
        (2, 2, N'2vs2', 4, N'DEMO MATCH 02 - Team đôi trình độ 3.0',        N'Ghép đội đánh đôi, có thể chọn ca sáng hoặc tối.',                N'2,3',   21.0310, 105.8080),
        (3, 3, N'2vs2', 4, N'DEMO MATCH 03 - Giao lưu sau giờ làm',         N'Trận giao lưu nhẹ nhàng sau giờ làm tại khu vực Cầu Giấy.',       N'3,4',   21.0340, 105.8000),
        (4, 4, N'1vs1', 2, N'DEMO MATCH 04 - Kèo đơn level 4',              N'Tìm đối thủ đánh đơn trình độ khá.',                              N'4,5',   21.0260, 105.8120),
        (5, 5, N'2vs2', 4, N'DEMO MATCH 05 - Nhóm mới chơi',                N'Nhóm thân thiện, phù hợp người mới và trung bình.',                N'5,6',   21.0380, 105.8150),
        (6, 6, N'2vs2', 4, N'DEMO MATCH 06 - Ca sáng trước giờ làm',        N'Chơi nhanh buổi sáng, kết thúc trước 08:30.',                      N'6,7',   21.0400, 105.8070),
        (7, 7, N'1vs1', 2, N'DEMO MATCH 07 - Luyện phản xạ đánh đơn',       N'Tập phản xạ và chiến thuật đánh đơn.',                            N'7,8',   21.0230, 105.7980),
        (8, 8, N'2vs2', 4, N'DEMO MATCH 08 - Đội đôi cuối tuần',            N'Tìm đủ đội hình 2vs2 cho cuối tuần.',                             N'8,9',   21.0200, 105.8100),
        (9, 1, N'2vs2', 4, N'DEMO MATCH 09 - Giao lưu nhiều khung giờ',     N'Có hai ca để cả nhóm dễ chốt lịch.',                              N'9,10',  21.0360, 105.8180),
        (10, 2, N'1vs1', 2, N'DEMO MATCH 10 - Kèo tối Cầu Giấy',            N'Tìm một người chơi ca tối, level 2 đến 4.',                       N'1,10',  21.0295, 105.7950);

    INSERT INTO [MATCH]
    (
        hostPlayerId,
        matchType,
        matchSkillLevel,
        requiredPlayerCount,
        status,
        title,
        note,
        province,
        ward,
        searchRadiusKm,
        searchLatitude,
        searchLongitude,
        availableDateFrom,
        availableDateTo,
        minSkillLevel,
        maxSkillLevel,
        preferredTimeStart,
        preferredTimeEnd,
        sharedVenues,
        createdAt
    )
    SELECT
        hostPlayer.playerId,
        source.MatchType,
        3,
        source.RequiredPlayerCount,
        N'Recruiting',
        source.Title,
        source.Note,
        N'Hà Nội',
        N'Cầu Giấy',
        10,
        source.Latitude,
        source.Longitude,
        DATEADD(day, source.Seq, CAST(GETDATE() AS date)),
        DATEADD(day, source.Seq + 7, CAST(GETDATE() AS date)),
        2,
        5,
        CAST(CASE WHEN source.Seq % 2 = 0 THEN N'06:30' ELSE N'07:00' END AS time),
        CAST(CASE WHEN source.Seq % 3 = 0 THEN N'21:00' ELSE N'20:30' END AS time),
        source.VenueIds,
        DATEADD(minute, -source.Seq, GETUTCDATE())
    FROM @DemoMatches source
    INNER JOIN @DemoPlayers hostSource ON hostSource.Seq = source.HostSeq
    INNER JOIN [USER] hostUser ON hostUser.email = hostSource.Email
    INNER JOIN [PLAYER] hostPlayer ON hostPlayer.userId = hostUser.userId;

    INSERT INTO [MATCH_AVAILABILITY_SLOT] (matchId, timeStart, timeEnd)
    SELECT
        m.matchId,
        slot.TimeStart,
        slot.TimeEnd
    FROM @DemoMatches source
    INNER JOIN [MATCH] m ON m.title = source.Title
    CROSS APPLY
    (
        VALUES
            (
                CAST(CASE WHEN source.Seq % 2 = 0 THEN N'06:30' ELSE N'07:00' END AS time),
                CAST(CASE WHEN source.Seq % 2 = 0 THEN N'08:30' ELSE N'09:00' END AS time)
            ),
            (
                CAST(CASE WHEN source.Seq % 3 = 0 THEN N'18:30' ELSE N'18:00' END AS time),
                CAST(CASE WHEN source.Seq % 3 = 0 THEN N'21:00' ELSE N'20:30' END AS time)
            )
    ) slot(TimeStart, TimeEnd);

    INSERT INTO [MATCH_PARTICIPANT]
        (matchId, playerId, status, isHost, requestedAt, respondedAt)
    SELECT
        m.matchId,
        hostPlayer.playerId,
        N'Approved',
        1,
        m.createdAt,
        m.createdAt
    FROM @DemoMatches source
    INNER JOIN [MATCH] m ON m.title = source.Title
    INNER JOIN @DemoPlayers hostSource ON hostSource.Seq = source.HostSeq
    INNER JOIN [USER] hostUser ON hostUser.email = hostSource.Email
    INNER JOIN [PLAYER] hostPlayer ON hostPlayer.userId = hostUser.userId;

    INSERT INTO [MATCH_PARTICIPANT]
        (matchId, playerId, status, isHost, requestedAt, respondedAt)
    SELECT
        m.matchId,
        invitedPlayer.playerId,
        N'Invited',
        0,
        DATEADD(minute, 1, m.createdAt),
        NULL
    FROM @DemoMatches source
    INNER JOIN [MATCH] m ON m.title = source.Title
    INNER JOIN @DemoPlayers invitedSource ON invitedSource.Seq = (source.HostSeq % 8) + 1
    INNER JOIN [USER] invitedUser ON invitedUser.email = invitedSource.Email
    INNER JOIN [PLAYER] invitedPlayer ON invitedPlayer.userId = invitedUser.userId;

    INSERT INTO [MATCH_PARTICIPANT]
        (matchId, playerId, status, isHost, requestedAt, respondedAt)
    SELECT
        m.matchId,
        pendingPlayer.playerId,
        N'Pending',
        0,
        DATEADD(minute, 2, m.createdAt),
        NULL
    FROM @DemoMatches source
    INNER JOIN [MATCH] m ON m.title = source.Title
    INNER JOIN @DemoPlayers pendingSource ON pendingSource.Seq = ((source.HostSeq + 1) % 8) + 1
    INNER JOIN [USER] pendingUser ON pendingUser.email = pendingSource.Email
    INNER JOIN [PLAYER] pendingPlayer ON pendingPlayer.userId = pendingUser.userId
    WHERE source.MatchType = N'2vs2';

    INSERT INTO [MATCH_PARTICIPANT]
        (matchId, playerId, status, isHost, requestedAt, respondedAt)
    SELECT
        m.matchId,
        approvedPlayer.playerId,
        N'Approved',
        0,
        DATEADD(minute, 3, m.createdAt),
        DATEADD(minute, 4, m.createdAt)
    FROM @DemoMatches source
    INNER JOIN [MATCH] m ON m.title = source.Title
    INNER JOIN @DemoPlayers approvedSource ON approvedSource.Seq = ((source.HostSeq + 2) % 8) + 1
    INNER JOIN [USER] approvedUser ON approvedUser.email = approvedSource.Email
    INNER JOIN [PLAYER] approvedPlayer ON approvedPlayer.userId = approvedUser.userId
    WHERE source.MatchType = N'2vs2';

    INSERT INTO [CONVERSATION] (conversationType, conversationName, createdAt, matchId)
    SELECT N'LobbyChat', m.title, m.createdAt, m.matchId
    FROM [MATCH] m
    WHERE m.title LIKE N'DEMO MATCH %';

    INSERT INTO [CONVERSATION_PARTICIPANT] (conversationId, userId, joinedAt)
    SELECT DISTINCT
        c.conversationId,
        p.userId,
        COALESCE(mp.respondedAt, mp.requestedAt)
    FROM [MATCH] m
    INNER JOIN [CONVERSATION] c ON c.matchId = m.matchId AND c.conversationType = N'LobbyChat'
    INNER JOIN [MATCH_PARTICIPANT] mp ON mp.matchId = m.matchId
    INNER JOIN [PLAYER] p ON p.playerId = mp.playerId
    WHERE m.title LIKE N'DEMO MATCH %'
      AND mp.status IN (N'Approved', N'Accepted');

    INSERT INTO [NOTIFICATION_LOG] (userId, message, isRead)
    SELECT
        p.userId,
        N'[DEMO MATCH] Bạn được mời tham gia "' + m.title + N'".',
        0
    FROM [MATCH] m
    INNER JOIN [MATCH_PARTICIPANT] mp ON mp.matchId = m.matchId AND mp.status = N'Invited'
    INNER JOIN [PLAYER] p ON p.playerId = mp.playerId
    WHERE m.title LIKE N'DEMO MATCH %';

    COMMIT TRANSACTION;

    SELECT
        m.matchId,
        m.title,
        hostUser.email AS hostEmail,
        m.matchType,
        m.status,
        COUNT(DISTINCT slot.matchAvailabilitySlotId) AS slotCount,
        COUNT(DISTINCT CASE WHEN mp.status = N'Invited' THEN mp.participantId END) AS invitedCount,
        COUNT(DISTINCT CASE WHEN mp.status = N'Pending' THEN mp.participantId END) AS pendingCount,
        COUNT(DISTINCT CASE WHEN mp.status IN (N'Approved', N'Accepted') THEN mp.participantId END) AS approvedCount
    FROM [MATCH] m
    INNER JOIN [PLAYER] hostPlayer ON hostPlayer.playerId = m.hostPlayerId
    INNER JOIN [USER] hostUser ON hostUser.userId = hostPlayer.userId
    LEFT JOIN [MATCH_AVAILABILITY_SLOT] slot ON slot.matchId = m.matchId
    LEFT JOIN [MATCH_PARTICIPANT] mp ON mp.matchId = m.matchId
    WHERE m.title LIKE N'DEMO MATCH %'
    GROUP BY m.matchId, m.title, hostUser.email, m.matchType, m.status
    ORDER BY m.title;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
