using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using RestaurantReservation.Data;
using RestaurantReservation.Models;
// The Razor pages in Pages/Table compile into a namespace called Table, which hides the model's short name here.
using DiningTable = RestaurantReservation.Models.Table;

namespace Restaurant_Reservation_and_Order_Management_System.Pages.Admin;

public class TableManagementModel(ApplicationDbContext db) : PageModel
{
    // Table sizes staff can add. Chairs are drawn half above and half below the table.
    public static readonly int[] SeatOptions = [2, 4, 6, 8, 10, 12];

    // Tables per row on the floor plan, used to place a new table after the last one.
    private const int FloorColumns = 6;

    public List<DiningTable> Tables { get; private set; } = [];

    public List<ReservationSummary> Reservations { get; private set; } = [];

    public string? SelectedTableId { get; private set; }

    // Reservations (any status) that would be deleted with each table because it is their only table,
    // so staff are warned before removing a booked table.
    public Dictionary<string, int> ReservationCounts { get; private set; } = [];

    // Reservations that also use other tables; removing the table only takes it off them.
    public Dictionary<string, int> SharedReservationCounts { get; private set; } = [];

    // Upcoming reservations for the sidebar: ones still needing a table first, then by time.
    public List<ReservationSummary> Queue { get; private set; } = [];

    // For each reservation in the queue, the tables free at its time (its own tables included).
    // Staff can combine several of them when one table is too small.
    public Dictionary<string, List<string>> FreeTables { get; private set; } = [];

    [TempData]
    public string? StatusMessage { get; set; }

    public async Task OnGetAsync(string? tableId)
    {
        Tables = await LoadTablesAsync();

        var reservations = await db.Reservations.AsNoTracking()
            .Include(reservation => reservation.Customer)
            .Include(reservation => reservation.ReservationTables)
            .Where(reservation => reservation.Status != ReservationStatus.Cancelled
                && reservation.Status != ReservationStatus.Completed)
            .ToListAsync();
        Reservations = reservations.Select(ReservationSummary.From).ToList();

        var links = await db.ReservationTables.AsNoTracking()
            .Select(link => new { link.TableId, link.ReservationId })
            .ToListAsync();
        var tablesPerReservation = links
            .GroupBy(link => link.ReservationId)
            .ToDictionary(group => group.Key, group => group.Count());
        ReservationCounts = links
            .Where(link => tablesPerReservation[link.ReservationId] == 1)
            .GroupBy(link => link.TableId)
            .ToDictionary(group => group.Key, group => group.Count());
        SharedReservationCounts = links
            .Where(link => tablesPerReservation[link.ReservationId] > 1)
            .GroupBy(link => link.TableId)
            .ToDictionary(group => group.Key, group => group.Count());

        var now = DateTime.Now;
        Queue = Reservations
            .Where(reservation => reservation.EndsAt > now)
            .OrderBy(reservation => reservation.HasTable)
            .ThenBy(reservation => reservation.ReservationAt)
            .ToList();
        FreeTables = Queue.ToDictionary(
            reservation => reservation.Id,
            reservation => Tables
                .Where(table => ReservationSummary.FindClash(Reservations, table.Id, reservation.ReservationAt, reservation.Id) is null)
                .Select(table => table.Id)
                .ToList());

        SelectedTableId = Tables.FirstOrDefault(table => table.Id == tableId)?.Id;
    }

    public async Task<IActionResult> OnPostAddTableAsync(int seats)
    {
        if (!SeatOptions.Contains(seats))
        {
            StatusMessage = "Choose how many seats the new table has.";
            return RedirectToPage();
        }

        var tables = await LoadTablesAsync();
        var last = tables.LastOrDefault();
        var (column, row) = last is null ? (1, 1)
            : last.CoordinateX < FloorColumns ? (last.CoordinateX + 1, last.CoordinateY)
            : (1, last.CoordinateY + 1);

        var table = new DiningTable
        {
            Id = NextTableId(tables),
            Capacity = seats,
            CoordinateX = column,
            CoordinateY = row
        };
        db.Tables.Add(table);
        await db.SaveChangesAsync();

        StatusMessage = $"Table {table.Id} added with {seats} seats.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostRemoveTableAsync(string? tableId)
    {
        var table = tableId is null ? null : await db.Tables.FindAsync(tableId);
        if (table is null)
        {
            StatusMessage = "That table was already removed.";
            return RedirectToPage();
        }

        // A reservation with only this table is removed with it (staff confirm this first);
        // one spread over several tables keeps its other tables.
        var reservations = await db.Reservations
            .Include(reservation => reservation.ReservationTables)
            .Where(reservation => reservation.ReservationTables.Any(link => link.TableId == table.Id))
            .ToListAsync();
        var deleted = reservations.Where(reservation => reservation.ReservationTables.Count == 1).ToList();
        db.Reservations.RemoveRange(deleted);
        db.ReservationTables.RemoveRange(reservations
            .Except(deleted)
            .SelectMany(reservation => reservation.ReservationTables)
            .Where(link => link.TableId == table.Id));
        db.Tables.Remove(table);
        await db.SaveChangesAsync();

        var shared = reservations.Count - deleted.Count;
        StatusMessage = $"Table {table.Id} removed"
            + (deleted.Count == 0 ? "" : $" with its {deleted.Count} reservation{(deleted.Count == 1 ? "" : "s")}")
            + (shared == 0 ? "" : $"; {shared} reservation{(shared == 1 ? " keeps its" : "s keep their")} other tables")
            + ".";
        return RedirectToPage();
    }

    // Seats a reservation at the given tables (several for a large party), or takes it off its tables
    // when tableIds is empty.
    public async Task<IActionResult> OnPostAssignAsync(string? reservationId, string[] tableIds)
    {
        var reservation = reservationId is null ? null : await db.Reservations
            .Include(item => item.Customer)
            .Include(item => item.ReservationTables)
            .FirstOrDefaultAsync(item => item.Id == reservationId);
        if (reservation is null)
        {
            StatusMessage = "That reservation no longer exists.";
            return RedirectToPage();
        }

        var summary = ReservationSummary.From(reservation);
        var label = $"{summary.CustomerName} ({summary.PartySize} guests, "
            + $"{summary.ReservationAt.ToString("ddd d MMM, h:mm tt", CultureInfo.CurrentCulture)})";

        var wanted = tableIds.Where(id => !string.IsNullOrEmpty(id)).Distinct().ToList();
        if (wanted.Count == 0)
        {
            reservation.ReservationTables.Clear();
            await db.SaveChangesAsync();
            StatusMessage = $"{label} no longer has a table.";
            return RedirectToPage();
        }

        var tables = await db.Tables.Where(table => wanted.Contains(table.Id)).ToListAsync();
        if (tables.Count != wanted.Count)
        {
            StatusMessage = "One of those tables no longer exists.";
            return RedirectToPage();
        }

        var seats = tables.Sum(table => table.Capacity);
        if (reservation.Pax > seats)
        {
            StatusMessage = $"{label} needs {reservation.Pax} seats, but {TablesLabel(wanted)} only seat{(wanted.Count == 1 ? "s" : "")} {seats}.";
            return RedirectToPage();
        }

        var others = (await db.Reservations.AsNoTracking()
                .Include(item => item.Customer)
                .Include(item => item.ReservationTables)
                .Where(item => item.Id != reservation.Id
                    && item.ReservationTables.Any(link => wanted.Contains(link.TableId)))
                .ToListAsync())
            .Select(ReservationSummary.From)
            .ToList();
        foreach (var tableId in wanted)
        {
            var clash = ReservationSummary.FindClash(others, tableId, summary.ReservationAt, reservation.Id);
            if (clash is not null)
            {
                StatusMessage = $"Table {tableId} is already booked by {clash.CustomerName} at "
                    + $"{clash.ReservationAt.ToString("h:mm tt", CultureInfo.CurrentCulture)}.";
                return RedirectToPage();
            }
        }

        reservation.ReservationTables.RemoveAll(link => !wanted.Contains(link.TableId));
        foreach (var tableId in wanted.Where(id => reservation.ReservationTables.All(link => link.TableId != id)))
        {
            // Id is set here, not on save: EF cannot track two new links that both have an empty Id.
            reservation.ReservationTables.Add(new ReservationTable { Id = Guid.NewGuid().ToString("N"), TableId = tableId });
        }
        await db.SaveChangesAsync();
        StatusMessage = $"{label} is now at {TablesLabel(wanted)}.";
        return RedirectToPage();
    }

    private static string TablesLabel(IReadOnlyCollection<string> tableIds) =>
        (tableIds.Count == 1 ? "table " : "tables ") + string.Join(" + ", tableIds);

    private Task<List<DiningTable>> LoadTablesAsync() =>
        db.Tables.AsNoTracking()
            .OrderBy(table => table.CoordinateY)
            .ThenBy(table => table.CoordinateX)
            .ThenBy(table => table.Id)
            .ToListAsync();

    // Tables are named T1, T2, ...; a new one takes the number after the highest in use.
    private static string NextTableId(IEnumerable<DiningTable> tables)
    {
        var highest = tables
            .Select(table => table.Id.StartsWith('T') && int.TryParse(table.Id[1..], out var number) ? number : 0)
            .DefaultIfEmpty(0)
            .Max();
        return $"T{highest + 1}";
    }
}
