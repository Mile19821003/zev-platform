using Zev.Api.Domain;

namespace Zev.Api.Finance;

public static class BillingCalculator
{
    public static decimal Quantity(TariffBasis basis, PropertyUnit unit) => basis switch
    {
        TariffBasis.SquareMeter => unit.AreaSquareMeters,
        TariffBasis.FixedUnit => 1m,
        TariffBasis.RegisteredMember => unit.RegisteredMembers,
        TariffBasis.Extraordinary => 1m,
        TariffBasis.OneTime => 1m,
        TariffBasis.ManualAmount => 1m,
        _ => throw new ArgumentOutOfRangeException(nameof(basis))
    };

    public static decimal Amount(decimal quantity, decimal rate, decimal share) =>
        decimal.Round(quantity * rate * share, 2, MidpointRounding.AwayFromZero);
}
