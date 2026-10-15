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

    // Every reservation per table, any status, so staff are warned before removing a booked table.
    public Dictionary<string, int> ReservationCounts { get; private set; } = [];

    // Upcoming reservations for the sidebar: ones still needing a table first, then by time.
    public List<ReservationSummary> Queue { get; private set; } = [];

    // For each reservation in the queue, the tables it can be moved to (big enough and free then).
    public Dictionary<string, List<string>> FittingTables { get; private set; } = [];

    [TempData]
    public string? StatusMessage { get; set; }

    public async Task OnGetAsync(string? tableId)
    {
        Tables = await LoadTablesAsync();

        var reservations = await db.Reservations.AsNoTracking()
            .Include(reservation => reservation.Customer)
            .Where(reservation => reservation.Status != ReservationStatus.Cancelled
                && reservation.Status != ReservationStatus.Completed)
            .ToListAsync();
        Reservations = reservations.Select(ReservationSummary.From).ToList();

        ReservationCounts = await db.Reservations
            .Where(reservation => reservation.TableId != null)
            .GroupBy(reservation => reservation.TableId!)
            .Select(group => new { TableId = group.Key, Count = group.Count() })
            .ToDictionaryAsync(item => item.TableId, item => item.Count);

        var now = DateTime.Now;
        Queue = Reservations
            .Where(reservation => reservation.EndsAt > now)
            .OrderBy(reservation => reservation.TableId is not null)
            .ThenBy(reservation => reservation.ReservationAt)
            .ToList();
        FittingTables = Queue.ToDictionary(
            reservation => reservation.Id,
            reservation => Tables
                .Where(table => table.Id != reservation.TableId
                    && table.Capacity >= reservation.PartySize
                    && ReservationSummary.FindClash(Reservations, table.Id, reservation.ReservationAt, reservation.Id) is null)
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

        // Reservations point at their table, so they are removed with it (staff confirm this first).
        var reservations = await db.Reservations
            .Where(reservation => reservation.TableId == table.Id)
            .ToListAsync();
        db.Reservations.RemoveRange(reservations);
        db.Tables.Remove(table);
        await db.SaveChangesAsync();

        StatusMessage = reservations.Count == 0
            ? $"Table {table.Id} removed."
            : $"Table {table.Id} removed with its {reservations.Count} reservation{(reservations.Count == 1 ? "" : "s")}.";
        return RedirectToPage();
    }

    // Puts a reservation on a table, or takes it off its table when tableId is empty.
    public async Task<IActionResult> OnPostAssignAsync(string? reservationId, string? tableId)
    {
        var reservation = reservationId is null ? null : await db.Reservations.FindAsync(reservationId);
        if (reservation is null)
        {
            StatusMessage = "That reservation no longer exists.";
            return RedirectToPage();
        }

        await db.Entry(reservation).Reference(item => item.Customer).LoadAsync();
        var summary = ReservationSummary.From(reservation);
        var label = $"{summary.CustomerName} ({summary.PartySize} guests, "
            + $"{summary.ReservationAt.ToString("ddd d MMM, h:mm tt", CultureInfo.CurrentCulture)})";

        if (string.IsNullOrEmpty(tableId))
        {
            reservation.TableId = null;
            await db.SaveChangesAsync();
            StatusMessage = $"{label} no longer has a table.";
            return RedirectToPage();
        }

        var table = await db.Tables.FindAsync(tableId);
        if (table is null)
        {
            StatusMessage = "That table no longer exists.";
            return RedirectToPage();
        }

        if (reservation.Pax > table.Capacity)
        {
            StatusMessage = $"Table {table.Id} only seats {table.Capacity}, so it can't take {label}.";
            return RedirectToPage();
        }

        var others = (await db.Reservations.AsNoTracking()
                .Include(item => item.Customer)
                .Where(item => item.TableId == table.Id)
                .ToListAsync())
            .Select(ReservationSummary.From);
        var clash = ReservationSummary.FindClash(others, table.Id, summary.ReservationAt, reservation.Id);
        if (clash is not null)
        {
            StatusMessage = $"Table {table.Id} is already booked by {clash.CustomerName} at "
                + $"{clash.ReservationAt.ToString("h:mm tt", CultureInfo.CurrentCulture)}.";
            return RedirectToPage();
        }

        reservation.TableId = table.Id;
        await db.SaveChangesAsync();
        StatusMessage = $"{label} is now at table {table.Id}.";
        return RedirectToPage();
    }

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
