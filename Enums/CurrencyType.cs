using System.ComponentModel.DataAnnotations;

namespace RestaurantReservation.Models.Enums
{
    public enum CurrencyType
    {
        [Display(Name = "RM", ShortName = "MYR")]
        MYR,

        [Display(Name = "$", ShortName = "USD")]
        USD,

        [Display(Name = "€", ShortName = "EUR")]
        EUR
    }
}