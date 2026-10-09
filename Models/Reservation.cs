using System.ComponentModel.DataAnnotations;

namespace Restaurant_Reservation_and_Order_Management_System.Models;

public class Reservation
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public int TableId { get; set; }

    [Required]
    [StringLength(80)]
    public string CustomerName { get; set; } = string.Empty;

    [StringLength(30)]
    public string? ContactNumber { get; set; }

    [Range(1, 20)]
    public int PartySize { get; set; }

    public DateTime ReservationAt { get; set; }

    [StringLength(500)]
    public string? Notes { get; set; }
}
