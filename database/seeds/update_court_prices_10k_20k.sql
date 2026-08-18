-- ============================================================
-- Cập nhật giá theo giờ (Hourly Price) cho toàn bộ Court từ 10.000đ - 20.000đ
-- ============================================================

-- 1. Cập nhật phân bổ giá theo số thứ tự sân:
-- Sân 1: 10.000đ/h
-- Sân 2: 15.000đ/h
-- Sân 3 trở lên: 20.000đ/h
UPDATE [COURT]
SET [hourlyPrice] = CASE 
    WHEN ([courtNumber] % 3) = 1 THEN 10000.00
    WHEN ([courtNumber] % 3) = 2 THEN 15000.00
    ELSE 20000.00
END;

-- 2. Kiểm tra lại kết quả:
SELECT 
    v.venueName,
    c.courtId,
    c.courtNumber,
    c.hourlyPrice,
    c.availabilityStatus
FROM [COURT] c
INNER JOIN [VENUE] v ON v.venueId = c.venueId
ORDER BY v.venueId, c.courtNumber;
