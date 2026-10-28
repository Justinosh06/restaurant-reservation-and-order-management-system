using Microsoft.EntityFrameworkCore;
using RestaurantReservation.Data;
using RestaurantReservation.Models;

namespace RestaurantReservation.Services;

public sealed class DiningSessionService
{
    private readonly ApplicationDbContext _db;

    public DiningSessionService(ApplicationDbContext db)
    {
        _db = db;
    }

    public Task<bool> HasActiveSessionAsync(string tableId)
    {
        return _db.Sessions
            .AsNoTracking()
            .AnyAsync(session => session.TableId == tableId && session.EndingTime == null);
    }

    public Task<Session?> GetActiveSessionAsync(string tableId)
    {
        return _db.Sessions
            .AsNoTracking()
            .SingleOrDefaultAsync(session => session.TableId == tableId && session.EndingTime == null);
    }

    public Task<bool> HasUnpaidOrdersAsync(string sessionId)
    {
        return _db.Orders
            .AsNoTracking()
            .AnyAsync(order =>
                order.SessionId == sessionId &&
                order.Status != OrderStatus.Completed &&
                order.Status != OrderStatus.Cancelled);
    }

    public async Task<int> GetCartItemCountAsync(string sessionId)
    {
        return await _db.CartItems
            .Where(item => item.SessionId == sessionId)
            .SumAsync(item => (int?)item.Quantity)
            ?? 0;
    }

    public async Task StartSessionAsync(string tableId)
    {
        if (await HasActiveSessionAsync(tableId))
        {
            return;
        }

        _db.Sessions.Add(new Session
        {
            TableId = tableId,
            StartingTime = DateTime.UtcNow
        });

        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            _db.ChangeTracker.Clear();
            if (await HasActiveSessionAsync(tableId))
            {
                return;
            }

            throw;
        }
    }

    // An order keeps its table's session open until it is paid for (Completed) or cancelled.
    public static bool IsOpen(Order order) =>
        order.Status != OrderStatus.Cancelled &&
        order.Status != OrderStatus.Completed;

    // Ends the table's session, or says why it can't: no session is open, or OpenOrders
    // (with their items) aren't finished yet, so staff can be told which ones.
    public async Task<EndSessionResult> EndSessionAsync(string tableId)
    {
        var session = await _db.Sessions
            .Include(session => session.Orders)
            .ThenInclude(order => order.OrderItems)
            .SingleOrDefaultAsync(session => session.TableId == tableId && session.EndingTime == null);

        if (session is null)
        {
            return new EndSessionResult(EndSessionOutcome.NoActiveSession, []);
        }

        var openOrders = session.Orders.Where(IsOpen).OrderBy(order => order.CreatedAt).ToList();
        if (openOrders.Count > 0)
        {
            return new EndSessionResult(EndSessionOutcome.OrdersOpen, openOrders);
        }

        session.EndingTime = DateTime.UtcNow;

        var cartItems = await _db.CartItems
            .Where(item => item.SessionId == session.Id)
            .ToListAsync();

        if (cartItems.Count > 0)
        {
            _db.CartItems.RemoveRange(cartItems);
        }

        await _db.SaveChangesAsync();
        return new EndSessionResult(EndSessionOutcome.Ended, []);
    }

    public async Task CompleteCheckedInReservationsAsync(Session session)
    {
        if (session.CustomerId is null)
        {
            return;
        }

        var reservations = await _db.Reservations
            .Include(reservation => reservation.ReservationTables)
            .Where(reservation =>
                reservation.Status == ReservationStatus.CheckedIn &&
                reservation.CustomerId == session.CustomerId &&
                reservation.ReservationTables.Any(link => link.TableId == session.TableId))
            .ToListAsync();

        foreach (var reservation in reservations)
        {
            var tableIds = reservation.ReservationTables
                .Select(link => link.TableId)
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            var otherActiveTableIds = await _db.Sessions
                .AsNoTracking()
                .Where(activeSession =>
                    activeSession.Id != session.Id &&
                    activeSession.EndingTime == null &&
                    tableIds.Contains(activeSession.TableId))
                .Select(activeSession => activeSession.TableId)
                .Distinct()
                .ToListAsync();

            if (tableIds.All(tableId => !otherActiveTableIds.Contains(tableId, StringComparer.Ordinal)))
            {
                reservation.Status = ReservationStatus.Completed;
            }
        }
    }
}

public enum EndSessionOutcome
{
    Ended,
    NoActiveSession,
    OrdersOpen
}

public sealed record EndSessionResult(EndSessionOutcome Outcome, IReadOnlyList<Order> OpenOrders);
