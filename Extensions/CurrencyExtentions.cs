using System.ComponentModel.DataAnnotations;
using System.Reflection;
using RestaurantReservation.Models.Enums;

namespace RestaurantReservation.Extensions
{
    public static class CurrencyExtensions
    {
        public static string GetSymbol(this CurrencyType currency)
        {
            return GetDisplayAttribute(currency)?.GetName() ?? currency.ToString();
        }

        public static string GetCode(this CurrencyType currency)
        {
            return GetDisplayAttribute(currency)?.GetShortName() ?? currency.ToString();
        }

        public static string FormatCents(this CurrencyType currency, int amountInCents)
        {
            decimal amount = amountInCents / 100m;
            return $"{currency.GetSymbol()}{amount:N2}";
        }

        private static DisplayAttribute? GetDisplayAttribute(CurrencyType currency)
        {
            return currency.GetType()
                .GetField(currency.ToString())?
                .GetCustomAttribute<DisplayAttribute>();
        }
    }
}