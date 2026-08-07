using AmroStockManager.Data;
using AmroStockManager.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace AmroStockManager.Services;

public class ResidentService(IDbContextFactory<AppDbContext> dbFactory)
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
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.Residents
            .OrderBy(r => r.IsCollaborator)
            .ThenBy(r => r.RoomNumber)
            .ThenBy(r => r.Name)
            .ToListAsync();
    }

    public async Task<Resident?> GetByRoomAsync(string roomNumber)
    {
        if (string.IsNullOrWhiteSpace(roomNumber)) return null;
        var room = roomNumber.Trim().ToUpper();
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.Residents.FirstOrDefaultAsync(r => r.RoomNumber.ToUpper() == room);
    }

    // Returns autocomplete suggestions in "#RoomNumber - Name" format.
    public async Task<IEnumerable<string>> GetSuggestionsAsync(string query)
    {
        if (string.IsNullOrWhiteSpace(query)) return [];
        var q = query.TrimStart('#').Trim().ToLower();
        await using var db = await dbFactory.CreateDbContextAsync();
        var matches = await db.Residents
            .Where(r => r.RoomNumber.ToLower().Contains(q) || r.Name.ToLower().Contains(q))
            .OrderBy(r => r.RoomNumber)
            .Take(10)
            .ToListAsync();
        return matches.Select(r => $"#{r.RoomNumber} - {r.Name}");
    }

    // Parses "#101 - João Silva" or "101" → "101".
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

    // Replaces all residents with the contents of the CSV stream.
    // Expected columns (with or without header): Nome, Quarto, Telemóvel, Colaborador
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

        var residents = new List<Resident>();
        int skipped = 0;

        for (int i = startLine; i < lines.Count; i++)
        {
            var parts = ParseCsvLine(lines[i]);
            if (parts.Length == 0 || string.IsNullOrWhiteSpace(parts[0])) { skipped++; continue; }

            var name  = parts[0].Trim();
            var room  = parts.Length > 1 ? parts[1].Trim().ToUpper() : string.Empty;
            var phone = parts.Length > 2 ? parts[2].Trim() : null;
            var collab = parts.Length > 3 &&
                         parts[3].Trim().ToLower() is "true" or "1" or "sim" or "yes" or "verdadeiro";

            if (string.IsNullOrWhiteSpace(name)) { skipped++; continue; }

            residents.Add(new Resident
            {
                Name          = name,
                RoomNumber    = room,
                PhoneNumber   = string.IsNullOrWhiteSpace(phone) ? null : phone,
                IsCollaborator = collab
            });
        }

        await using var db = await dbFactory.CreateDbContextAsync();
        await db.Residents.ExecuteDeleteAsync();
        db.Residents.AddRange(residents);
        await db.SaveChangesAsync();

        return (residents.Count, skipped);
    }

    public async Task DeleteResidentAsync(int id)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        await db.Residents.Where(r => r.Id == id).ExecuteDeleteAsync();
    }

    public async Task DeleteAllAsync()
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        await db.Residents.ExecuteDeleteAsync();
    }

    private static string[] ParseCsvLine(string line)
    {
        // Handles quoted fields (e.g. "Silva, João")
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
