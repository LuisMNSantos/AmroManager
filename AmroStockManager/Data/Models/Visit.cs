using System.Text.Json.Serialization;

namespace AmroStockManager.Data.Models;

public class Visit
{
    [JsonPropertyName("sync_id")]       public string    Id           { get; set; } = string.Empty;
    [JsonPropertyName("visitor_name")]  public string    VisitorName  { get; set; } = string.Empty;
    [JsonPropertyName("room_number")]   public string    RoomNumber   { get; set; } = string.Empty;
    [JsonPropertyName("registered_by")] public string?   RegisteredBy { get; set; }
    [JsonPropertyName("checked_in_at")] public DateTime  CheckedInAt  { get; set; }
    [JsonPropertyName("checked_out_at")]public DateTime? CheckedOutAt { get; set; }
    [JsonPropertyName("overnights")]    public int       Overnights   { get; set; }
    [JsonPropertyName("notes")]         public string?   Notes        { get; set; }
    [JsonPropertyName("is_deleted")]    public bool      IsDeleted    { get; set; }
    [JsonPropertyName("created_at")]    public DateTime  CreatedAt    { get; set; }
    [JsonPropertyName("updated_at")]    public DateTime  UpdatedAt    { get; set; }

    [JsonIgnore] public bool IsActive => CheckedOutAt is null;

    [JsonIgnore]
    public int LiveOvernights => CheckedOutAt.HasValue
        ? Overnights
        : ComputeOvernights(CheckedInAt, DateTime.UtcNow);

    public static int ComputeOvernights(DateTime checkIn, DateTime checkOut)
    {
        var localIn  = (checkIn.Kind  == DateTimeKind.Utc ? checkIn.ToLocalTime()  : checkIn).Date;
        var localOut = (checkOut.Kind == DateTimeKind.Utc ? checkOut.ToLocalTime() : checkOut).Date;
        return Math.Max(0, (localOut - localIn).Days);
    }
}
