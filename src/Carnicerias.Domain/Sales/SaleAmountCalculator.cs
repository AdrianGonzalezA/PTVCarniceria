namespace Carnicerias.Domain.Sales;

public enum SaleTaxTreatment
{
    Taxed,
    Exempt,
    NotTaxed
}

public sealed record SaleAmountInput(Guid LineId, decimal Quantity, decimal UnitPrice,
    decimal LineDiscount, SaleTaxTreatment TaxTreatment, decimal TaxRatePercent);

public sealed record SaleLineAmount(Guid LineId, decimal GrossBeforeDiscount,
    decimal LineDiscount, decimal OrderDiscount, decimal Total,
    decimal TaxableBase, decimal TaxAmount, decimal ExemptAmount,
    decimal NotTaxedAmount, SaleTaxTreatment TaxTreatment, decimal TaxRatePercent);

public sealed record SaleAmountResult(IReadOnlyList<SaleLineAmount> Lines,
    decimal GrossBeforeDiscount, decimal TotalDiscount, decimal Total,
    decimal TaxableBase, decimal TaxAmount, decimal ExemptAmount,
    decimal NotTaxedAmount);

/// <summary>Calculates fiscal amounts from final prices; it does not authorize a tax rate or issue invoices.</summary>
public static class SaleAmountCalculator
{
    public static SaleAmountResult Calculate(IReadOnlyList<SaleAmountInput> inputs,
        decimal orderDiscount)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        if (inputs.Count is < 1 or > 500 || inputs.Select(line => line.LineId).Distinct().Count() != inputs.Count)
            throw new ArgumentException("A sale requires one to 500 distinct lines.", nameof(inputs));
        if (!Money(orderDiscount) || orderDiscount < 0)
            throw new ArgumentOutOfRangeException(nameof(orderDiscount));

        var priced = inputs.Select(input =>
        {
            if (input.LineId == Guid.Empty || input.Quantity <= 0 ||
                decimal.Round(input.Quantity, 3) != input.Quantity ||
                input.UnitPrice <= 0 || !Money(input.UnitPrice) ||
                !Enum.IsDefined(input.TaxTreatment) ||
                input.TaxRatePercent is < 0 or > 100 ||
                decimal.Round(input.TaxRatePercent, 2) != input.TaxRatePercent ||
                (input.TaxTreatment != SaleTaxTreatment.Taxed && input.TaxRatePercent != 0))
                throw new ArgumentOutOfRangeException(nameof(inputs));
            var gross = Round(input.Quantity * input.UnitPrice);
            if (!Money(input.LineDiscount) || input.LineDiscount < 0 || input.LineDiscount > gross)
                throw new ArgumentOutOfRangeException(nameof(inputs));
            return new PricedLine(input, gross, gross - input.LineDiscount);
        }).ToArray();
        var available = priced.Sum(line => line.AfterLineDiscount);
        if (available <= 0 || orderDiscount >= available)
            throw new ArgumentOutOfRangeException(nameof(orderDiscount),
                "Discounts cannot consume the entire sale.");

        var allocatedCents = AllocateDiscount(priced, orderDiscount, available);
        var lines = priced.Select(line =>
        {
            var total = line.AfterLineDiscount - allocatedCents.GetValueOrDefault(line.Input.LineId) / 100m;
            var taxableBase = line.Input.TaxTreatment == SaleTaxTreatment.Taxed
                ? Round(total / (1 + line.Input.TaxRatePercent / 100m)) : 0m;
            var tax = line.Input.TaxTreatment == SaleTaxTreatment.Taxed ? total - taxableBase : 0m;
            return new SaleLineAmount(line.Input.LineId, line.Gross,
                line.Input.LineDiscount, line.AfterLineDiscount - total, total,
                taxableBase, tax,
                line.Input.TaxTreatment == SaleTaxTreatment.Exempt ? total : 0m,
                line.Input.TaxTreatment == SaleTaxTreatment.NotTaxed ? total : 0m,
                line.Input.TaxTreatment, line.Input.TaxRatePercent);
        }).ToArray();
        return new SaleAmountResult(lines, priced.Sum(line => line.Gross),
            priced.Sum(line => line.Input.LineDiscount) + orderDiscount,
            lines.Sum(line => line.Total), lines.Sum(line => line.TaxableBase),
            lines.Sum(line => line.TaxAmount), lines.Sum(line => line.ExemptAmount),
            lines.Sum(line => line.NotTaxedAmount));
    }

    private static Dictionary<Guid, long> AllocateDiscount(PricedLine[] lines,
        decimal orderDiscount, decimal available)
    {
        var cents = decimal.ToInt64(orderDiscount * 100m);
        var totalCents = decimal.ToInt64(available * 100m);
        var portions = lines.Select(line =>
        {
            var capacity = decimal.ToInt64(line.AfterLineDiscount * 100m);
            var exact = cents * (decimal)capacity / totalCents;
            var floor = decimal.ToInt64(decimal.Floor(exact));
            return new DiscountPortion(line.Input.LineId, floor, capacity, exact - floor);
        }).ToArray();
        var remainder = cents - portions.Sum(item => item.Cents);
        foreach (var portion in portions.Where(item => item.Cents < item.Capacity)
                     .OrderByDescending(item => item.Fraction).ThenBy(item => item.LineId))
        {
            if (remainder == 0) break;
            portion.Cents++;
            remainder--;
        }
        if (remainder != 0) throw new InvalidOperationException("Discount allocation did not reconcile.");
        return portions.ToDictionary(item => item.LineId, item => item.Cents);
    }

    private static bool Money(decimal value) => value <= 9_999_999_999.99m &&
        decimal.Round(value, 2) == value;

    private static decimal Round(decimal value) => decimal.Round(value, 2,
        MidpointRounding.AwayFromZero);

    private sealed record PricedLine(SaleAmountInput Input, decimal Gross,
        decimal AfterLineDiscount);

    private sealed class DiscountPortion(Guid lineId, long cents, long capacity,
        decimal fraction)
    {
        public Guid LineId { get; } = lineId;
        public long Cents { get; set; } = cents;
        public long Capacity { get; } = capacity;
        public decimal Fraction { get; } = fraction;
    }
}
