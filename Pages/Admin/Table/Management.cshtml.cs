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

        // Reservations point at their table, so a booked table cannot be removed.
        if (await db.Reservations.AnyAsync(reservation => reservation.TableId == table.Id))
        {
            StatusMessage = $"Table {table.Id} has reservations. Delete or move them before removing the table.";
            return RedirectToPage(new { tableId = table.Id });
        }

        db.Tables.Remove(table);
        await db.SaveChangesAsync();

        StatusMessage = $"Table {table.Id} removed.";
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
