using AmroStockManager.Data.Models;

namespace AmroStockManager.Services;

public class ResidentService(SupabaseClient db)
{
    private static readonly string PinFile = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "AmroStockManager", "admin.pin");

    public string GetPin() =>
        File.Exists(PinFile) ? File.ReadAllText(PinFile).Trim() : "1234";

    public void SavePin(string pin) =>
        File.WriteAllText(PinFile, pin.Trim());

    public async Task<List<Resident>> GetAllAsync()
    {
        return await db.GetAsync<Resident>("residents",
            "is_deleted=eq.false&order=is_collaborator.asc,room_number.asc,name.asc");
    }

    public async Task<Resident?> GetByRoomAsync(string roomNumber)
    {
        if (string.IsNullOrWhiteSpace(roomNumber)) return null;
        var room = Uri.EscapeDataString(roomNumber.Trim().ToUpper());
        var list = await db.GetAsync<Resident>("residents", $"is_deleted=eq.false&room_number=eq.{room}&limit=1");
        return list.FirstOrDefault();
    }

    public async Task<IEnumerable<string>> GetSuggestionsAsync(string query)
    {
        if (string.IsNullOrWhiteSpace(query)) return [];
        var q = Uri.EscapeDataString(query.TrimStart('#').Trim());
        var residents = await db.GetAsync<Resident>("residents",
            $"is_deleted=eq.false&or=(room_number.ilike.*{q}*,name.ilike.*{q}*)&order=room_number.asc&limit=10");
        return residents.Select(r => $"#{r.RoomNumber} - {r.Name}");
    }

    public static string ParseRoomNumber(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return string.Empty;
        input = input.Trim();
        if (input.StartsWith('#'))
        {
            var dash = input.IndexOf(" - ");
            return (dash > 0 ? input[1..dash] : input[1..]).Trim().ToUpper();
        }
        return input.ToUpper();
    }

    public async Task<(int Imported, int Skipped)> ImportFromCsvAsync(Stream csvStream)
    {
        using var reader = new StreamReader(csvStream);
        var lines = new List<string>();
        while (!reader.EndOfStream)
        {
            var line = await reader.ReadLineAsync();
            if (line is not null) lines.Add(line);
        }

        int startLine = 0;
        if (lines.Count > 0)
        {
            var first = lines[0].ToLower();
            if (first.Contains("nome") || first.Contains("name") || first.Contains("quarto"))
                startLine = 1;
        }

        var residents = new List<object>();
        int skipped = 0;

        for (int i = startLine; i < lines.Count; i++)
        {
            var parts = ParseCsvLine(lines[i]);
            if (parts.Length == 0 || string.IsNullOrWhiteSpace(parts[0])) { skipped++; continue; }

            var name   = parts[0].Trim();
            var room   = parts.Length > 1 ? parts[1].Trim().ToUpper() : string.Empty;
            var phone  = parts.Length > 2 ? parts[2].Trim() : null;
            var collab = parts.Length > 3 &&
                         parts[3].Trim().ToLower() is "true" or "1" or "sim" or "yes" or "verdadeiro";

            if (string.IsNullOrWhiteSpace(name)) { skipped++; continue; }

            residents.Add(new
            {
                sync_id         = Guid.NewGuid().ToString(),
                name            = name,
                room_number     = room,
                phone_number    = string.IsNullOrWhiteSpace(phone) ? (string?)null : phone,
                is_collaborator = collab,
                is_deleted      = false,
                updated_at      = DateTime.UtcNow
            });
        }

        await db.PatchAsync("residents", "is_deleted=eq.false", new { is_deleted = true, updated_at = DateTime.UtcNow });

        foreach (var r in residents)
            await db.InsertAsync<Resident>("residents", r);

        return (residents.Count, skipped);
    }

    public async Task DeleteResidentAsync(string id)
    {
        await db.PatchAsync("residents", $"sync_id=eq.{id}", new { is_deleted = true, updated_at = DateTime.UtcNow });
    }

    public async Task DeleteAllAsync()
    {
        await db.PatchAsync("residents", "is_deleted=eq.false", new { is_deleted = true, updated_at = DateTime.UtcNow });
    }

    private static string[] ParseCsvLine(string line)
    {
        var result = new List<string>();
        bool inQuotes = false;
        var current = new System.Text.StringBuilder();
        foreach (char c in line)
        {
            if (c == '"') { inQuotes = !inQuotes; }
            else if (c == ',' && !inQuotes) { result.Add(current.ToString()); current.Clear(); }
            else { current.Append(c); }
        }
        result.Add(current.ToString());
        return [.. result];
    }
}
