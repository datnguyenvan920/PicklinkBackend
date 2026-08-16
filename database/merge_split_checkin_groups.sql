/*
    Merges check-in groups that were split apart by the multi-court booking bug.

    A booking that reserved several courts for the same hour used to write its slots interleaved by
    time, and the grouping loop only remembered one open group, so every slot became its own
    check-in group. A one-hour block on two courts ended up as four groups and four check-in codes
    instead of two. MatchService.Open now orders slots by court before grouping, so new bookings are
    correct; this script repairs the rows written before that fix.

    What it does, per booking and court: finds runs of groups where one ends exactly where the next
    begins, keeps the earliest group (and therefore its check-in code), stretches it to cover the
    whole run, repoints the dependent rows, and deletes the redundant groups.

    Safety:
      - Dry run by default. Set @Commit = 1 to actually apply.
      - A run is skipped when its groups no longer agree: different check-in statuses, or any group
        already verified, checked in or marked no-show. Merging those would destroy check-in
        history, so they are listed for manual review instead.
      - Re-running after a successful apply is a no-op: no touching runs remain.
*/

SET NOCOUNT ON;
SET XACT_ABORT ON;

DECLARE @Commit bit = 0;   -- <<< set to 1 to apply the merge

BEGIN TRANSACTION;

IF OBJECT_ID('tempdb..#member') IS NOT NULL DROP TABLE #member;
IF OBJECT_ID('tempdb..#plan') IS NOT NULL DROP TABLE #plan;

/* Number each run of touching groups within a booking+court (gaps and islands). */
WITH ordered AS (
    SELECT
        bookingCheckInGroupId, bookingId, courtId, startTime, endTime,
        checkInStatus, checkedInAt, noShowAt, codeVerifiedAt,
        CASE
            WHEN LAG(endTime) OVER (PARTITION BY bookingId, courtId ORDER BY startTime) = startTime
            THEN 0 ELSE 1
        END AS isRunStart
    FROM BOOKING_CHECKIN_GROUP
),
runs AS (
    SELECT *,
        SUM(isRunStart) OVER (
            PARTITION BY bookingId, courtId
            ORDER BY startTime
            ROWS UNBOUNDED PRECEDING
        ) AS runNo
    FROM ordered
)
SELECT * INTO #member FROM runs;

/* One row per multi-group run, with the survivor and whether it is safe to merge. */
SELECT
    grouped.bookingId,
    grouped.courtId,
    grouped.runNo,
    grouped.memberCount,
    grouped.runStart,
    grouped.runEnd,
    survivor.bookingCheckInGroupId AS survivorId,
    survivor.checkInCode           AS survivorCode,
    CAST(CASE WHEN grouped.statusVariants = 1 AND grouped.actedOnCount = 0
              THEN 1 ELSE 0 END AS bit) AS isEligible
INTO #plan
FROM (
    SELECT
        bookingId, courtId, runNo,
        COUNT(*)                       AS memberCount,
        MIN(startTime)                 AS runStart,
        MAX(endTime)                   AS runEnd,
        COUNT(DISTINCT checkInStatus)  AS statusVariants,
        SUM(CASE WHEN checkedInAt IS NOT NULL
                   OR noShowAt IS NOT NULL
                   OR codeVerifiedAt IS NOT NULL
                 THEN 1 ELSE 0 END)    AS actedOnCount
    FROM #member
    GROUP BY bookingId, courtId, runNo
    HAVING COUNT(*) > 1
) AS grouped
CROSS APPLY (
    SELECT TOP 1 g.bookingCheckInGroupId, g.checkInCode
    FROM BOOKING_CHECKIN_GROUP g
    JOIN #member m ON m.bookingCheckInGroupId = g.bookingCheckInGroupId
    WHERE m.bookingId = grouped.bookingId
      AND m.courtId   = grouped.courtId
      AND m.runNo     = grouped.runNo
    ORDER BY m.startTime, m.bookingCheckInGroupId
) AS survivor;

PRINT '--- Runs found ---';
SELECT
    bookingId, courtId, memberCount,
    CONVERT(varchar(16), runStart, 120) AS runStart,
    CONVERT(varchar(16), runEnd, 120)   AS runEnd,
    survivorId, survivorCode,
    CASE WHEN isEligible = 1 THEN 'merge' ELSE 'SKIPPED - check-in history differs' END AS action
FROM #plan
ORDER BY bookingId, courtId, runStart;

DECLARE @eligible int = (SELECT COUNT(*) FROM #plan WHERE isEligible = 1);
DECLARE @skipped  int = (SELECT COUNT(*) FROM #plan WHERE isEligible = 0);
DECLARE @removed  int = (
    SELECT COUNT(*)
    FROM #member m
    JOIN #plan p ON p.bookingId = m.bookingId AND p.courtId = m.courtId AND p.runNo = m.runNo
    WHERE p.isEligible = 1 AND m.bookingCheckInGroupId <> p.survivorId
);

/* 1. Stretch each survivor over its whole run. */
UPDATE g
SET g.endTime   = p.runEnd,
    g.startTime = p.runStart,
    g.updatedAt = GETUTCDATE()
FROM BOOKING_CHECKIN_GROUP g
JOIN #plan p ON p.survivorId = g.bookingCheckInGroupId
WHERE p.isEligible = 1;

/* 2. Move every slot of the run onto the survivor. */
UPDATE s
SET s.checkInGroupId = p.survivorId
FROM BOOKING_SLOT s
JOIN #member m ON m.bookingCheckInGroupId = s.checkInGroupId
JOIN #plan   p ON p.bookingId = m.bookingId AND p.courtId = m.courtId AND p.runNo = m.runNo
WHERE p.isEligible = 1
  AND s.checkInGroupId <> p.survivorId;

/* 3. Move match slot absences too, or the delete below would violate its foreign key. */
UPDATE a
SET a.bookingCheckInGroupId = p.survivorId
FROM MATCH_SLOT_ABSENCE a
JOIN #member m ON m.bookingCheckInGroupId = a.bookingCheckInGroupId
JOIN #plan   p ON p.bookingId = m.bookingId AND p.courtId = m.courtId AND p.runNo = m.runNo
WHERE p.isEligible = 1
  AND a.bookingCheckInGroupId <> p.survivorId;

/* 4. Drop the now-redundant groups and their surplus check-in codes. */
DELETE g
FROM BOOKING_CHECKIN_GROUP g
JOIN #member m ON m.bookingCheckInGroupId = g.bookingCheckInGroupId
JOIN #plan   p ON p.bookingId = m.bookingId AND p.courtId = m.courtId AND p.runNo = m.runNo
WHERE p.isEligible = 1
  AND g.bookingCheckInGroupId <> p.survivorId;

PRINT '--- Result ---';
PRINT CONCAT('runs merged        : ', @eligible);
PRINT CONCAT('groups removed     : ', @removed);
PRINT CONCAT('runs skipped       : ', @skipped);

/* Nothing should touch anything else afterwards; prove it before committing. */
DECLARE @remaining int = (
    SELECT COUNT(*)
    FROM BOOKING_CHECKIN_GROUP a
    JOIN BOOKING_CHECKIN_GROUP b
      ON b.bookingId = a.bookingId AND b.courtId = a.courtId AND b.startTime = a.endTime
);
PRINT CONCAT('touching runs left : ', @remaining, ' (expected: only skipped ones)');

DECLARE @orphanSlots int = (
    SELECT COUNT(*) FROM BOOKING_SLOT s
    WHERE s.checkInGroupId IS NOT NULL
      AND NOT EXISTS (SELECT 1 FROM BOOKING_CHECKIN_GROUP g WHERE g.bookingCheckInGroupId = s.checkInGroupId)
);
IF @orphanSlots > 0
BEGIN
    PRINT CONCAT('ABORTING - orphaned booking slots: ', @orphanSlots);
    ROLLBACK TRANSACTION;
END
ELSE IF @Commit = 1
BEGIN
    COMMIT TRANSACTION;
    PRINT 'COMMITTED.';
END
ELSE
BEGIN
    ROLLBACK TRANSACTION;
    PRINT 'DRY RUN - rolled back. Set @Commit = 1 to apply.';
END
