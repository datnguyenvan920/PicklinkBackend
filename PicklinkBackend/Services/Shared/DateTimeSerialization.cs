using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace PicklinkBackend.Services.Shared;

/// <summary>
/// EF value converter đánh dấu giá trị đọc từ DB là UTC. SQL Server trả cột datetime về CLR với
/// DateTimeKind.Unspecified; phần lớn cột thời gian trong hệ thống được ghi bằng DateTime.UtcNow nên
/// thực chất là UTC. Đánh dấu Kind=Utc để tầng serialize biết đây là UTC mà đổi sang giờ Việt Nam.
/// </summary>
public sealed class UtcDateTimeConverter : ValueConverter<DateTime, DateTime>
{
    public UtcDateTimeConverter()
        : base(clr => clr, db => DateTime.SpecifyKind(db, DateTimeKind.Utc))
    {
    }
}

/// <summary>
/// Ngược lại với <see cref="UtcDateTimeConverter"/>: giữ Kind=Unspecified cho các cột lưu "giờ Việt Nam
/// wall-clock" (giờ đặt sân/lịch thi đấu). Áp riêng cho những cột này để chúng KHÔNG bị đổi múi giờ.
/// </summary>
public sealed class WallClockDateTimeConverter : ValueConverter<DateTime, DateTime>
{
    public WallClockDateTimeConverter()
        : base(clr => clr, db => DateTime.SpecifyKind(db, DateTimeKind.Unspecified))
    {
    }
}

/// <summary>
/// Serialize DateTime ra JSON theo giờ Việt Nam (wall-clock), khớp với cách frontend đang hiển thị.
/// - Kind=Utc  → đổi sang giờ VN rồi ghi (không kèm 'Z').
/// - Unspecified/Local → đã là giờ VN wall-clock, ghi nguyên trạng.
/// </summary>
public sealed class VietnamDateTimeJsonConverter : JsonConverter<DateTime>
{
    private const string Format = "yyyy-MM-dd'T'HH:mm:ss.fff";

    public override DateTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => reader.GetDateTime();

    public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options)
    {
        var vietnam = value.Kind == DateTimeKind.Utc ? VietnamTime.FromUtc(value) : value;
        writer.WriteStringValue(vietnam.ToString(Format, CultureInfo.InvariantCulture));
    }
}
