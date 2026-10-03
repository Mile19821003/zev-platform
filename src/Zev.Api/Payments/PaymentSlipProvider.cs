using System.Globalization;
using Zev.Api.Reporting;

namespace Zev.Api.Payments;

public sealed record PaymentSlipData(string Beneficiary, string AccountNumber, string Payer,
    string Reference, decimal Amount, string CurrencyCode, DateOnly DueDate, string? Purpose = null);

public interface IPaymentQrProvider
{
    byte[]? Generate(PaymentSlipData data);
}

public sealed class UnconfiguredPaymentQrProvider : IPaymentQrProvider
{
    public byte[]? Generate(PaymentSlipData data) => null;
}

public interface IPaymentSlipProvider
{
    byte[] Generate(PaymentSlipData data);
}

public sealed class GenericPdfPaymentSlipProvider(IPaymentQrProvider qrProvider) : IPaymentSlipProvider
{
    public byte[] Generate(PaymentSlipData data)
    {
        if (qrProvider.Generate(data) is not null)
            throw new NotSupportedException("QR rendering requires an approved payment-slip provider.");
        var lines = new[]
        {
            "INFORMATIVNA UPLATNICA",
            $"Primalac: {data.Beneficiary}",
            $"Racun primaoca: {data.AccountNumber}",
            $"Uplatilac: {data.Payer}",
            $"Poziv na broj: {data.Reference}",
            $"Svrha: {data.Purpose ?? "Uplata zaduzenja"}",
            $"Iznos: {data.Amount.ToString("0.00", CultureInfo.InvariantCulture)} {data.CurrencyCode}",
            $"Dospijece: {data.DueDate:dd.MM.yyyy}",
            "QR kod nije konfigurisan."
        };
        return SimplePdfWriter.Write(lines);
    }
}
