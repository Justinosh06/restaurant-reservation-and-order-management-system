using System.Text.Json;
using Restaurant_Reservation_and_Order_Management_System.Models;

namespace Restaurant_Reservation_and_Order_Management_System.Data;

public sealed class ReservationStore
{
    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web) { WriteIndented = true };

    private readonly string _filePath;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public ReservationStore(IWebHostEnvironment environment)
    {
        _filePath = Path.Combine(environment.ContentRootPath, "Data", "reservations.json");
    }

    public async Task<List<Reservation>> GetAllAsync()
    {
        await _gate.WaitAsync();
        try
        {
            return await ReadAsync();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task AddAsync(Reservation reservation)
    {
        await _gate.WaitAsync();
        try
        {
            var reservations = await ReadAsync();
            reservations.Add(reservation);
            await WriteAsync(reservations);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<bool> UpdateAsync(Reservation reservation)
    {
        await _gate.WaitAsync();
        try
        {
            var reservations = await ReadAsync();
            var index = reservations.FindIndex(item => item.Id == reservation.Id);
            if (index < 0)
            {
                return false;
            }

            reservations[index] = reservation;
            await WriteAsync(reservations);
            return true;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<bool> DeleteAsync(Guid id)
    {
        await _gate.WaitAsync();
        try
        {
            var reservations = await ReadAsync();
            if (reservations.RemoveAll(item => item.Id == id) != 1)
            {
                return false;
            }

            await WriteAsync(reservations);
            return true;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<List<Reservation>> ReadAsync()
    {
        if (!File.Exists(_filePath))
        {
            return new List<Reservation>();
        }

        await using var stream = File.OpenRead(_filePath);
        return await JsonSerializer.DeserializeAsync<List<Reservation>>(stream, JsonOptions)
            ?? new List<Reservation>();
    }

    private async Task WriteAsync(List<Reservation> reservations)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
        await using var stream = File.Create(_filePath);
        await JsonSerializer.SerializeAsync(stream, reservations, JsonOptions);
    }
}
