using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using RestaurantReservation.Models.Enums;

namespace RestaurantReservation.Models
{
    public enum TransactionStatus
    {
        Pending,
        Processing,
        Succeeded,
        Failed,
        Refunded
    }

    public enum PaymentMethod
    {
        Card,
        Fpx,
        EWallet,
        GrabPay,
        Cash
    }

    public class Transaction
    {
        [Key]
        [Required(ErrorMessage = "Transaction ID is required.")]
        public string Id { get; set; } = string.Empty;

        [Required]
        [Range(0, 10000000, ErrorMessage = "Amount must be positive.")]
        public int Amount { get; set; }

        [NotMapped]
        public decimal AmountInMainUnit => Amount / 100m;

        [Required]
        public CurrencyType Currency { get; set; }

        [Required]
        public TransactionStatus Status { get; set; }

        [Required]
        public PaymentMethod PaymentMethod { get; set; }

        [Required]
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public string? MetadataJson { get; set; }

        // Foreign Key
        [Required]
        public string OrderId { get; set; } = string.Empty;

        [ForeignKey(nameof(OrderId))]
        public Order? Order { get; set; }
    }
}