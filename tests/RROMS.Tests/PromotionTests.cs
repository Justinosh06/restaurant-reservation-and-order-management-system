using FluentAssertions;
using RestaurantReservation.Models;

namespace RROMS.Tests;

public class PromotionTests
{
    [Fact]
    public void Percentage_discount_rounds_to_nearest_cent_and_respects_minimum_spend()
    {
        var promotion = new Promotion
        {
            DiscountType = PromotionDiscountType.Percentage,
            DiscountValue = 20,
            MinimumSpend = 1000
        };

        promotion.DiscountOn(999).Should().Be(0);
        promotion.DiscountOn(1001).Should().Be(200);
        promotion.DiscountOn(1003).Should().Be(201);
    }

    [Fact]
    public void Fixed_discount_is_capped_at_the_order_subtotal()
    {
        var promotion = new Promotion
        {
            DiscountType = PromotionDiscountType.FixedAmount,
            DiscountValue = 2000
        };

        promotion.DiscountOn(1500).Should().Be(1500);
        promotion.DiscountOn(0).Should().Be(0);
    }
}
