using System.ComponentModel.DataAnnotations;
using System.Reflection;

namespace RestaurantReservation.Infrastructure.Admin;

public static class ModelRules
{
    public static int MaxLength<T>(string propertyName) =>
        typeof(T).GetProperty(propertyName)?.GetCustomAttribute<StringLengthAttribute>()?.MaximumLength
        ?? throw new InvalidOperationException($"{typeof(T).Name}.{propertyName} has no [StringLength].");

    public static string? FirstError(object model, params string[] propertyNames)
    {
        foreach (var name in propertyNames)
        {
            var value = model.GetType().GetProperty(name)?.GetValue(model);
            var results = new List<ValidationResult>();
            if (!Validator.TryValidateProperty(value, new ValidationContext(model) { MemberName = name }, results))
            {
                return results[0].ErrorMessage;
            }
        }
        return null;
    }
}
