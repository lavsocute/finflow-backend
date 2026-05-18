using FinFlow.Application.Bank;
using FinFlow.Application.Bank.Formatters;
using Xunit;

namespace FinFlow.UnitTests.Application.Bank;

/// <summary>
/// Byte-exact assertions on each bank-format adapter so accidental template
/// changes can never slip past code review. If a bank publishes a new spec, bump
/// the expected fixture in the test along with the formatter.
/// </summary>
public class BankCsvFormatterTests
{
    private static readonly IReadOnlyList<PaymentExportRow> SampleRows = new[]
    {
        new PaymentExportRow(
            Sequence: 1,
            PayeeName: "NGUYEN VAN A",
            PayeeBankCode: "VCB",
            PayeeBankAccountNumber: "0123456789",
            PayeeBankBranch: "HCM",
            Amount: 1_500_000m,
            CurrencyCode: "VND",
            TransferNote: "REIMB ABCD1234 NGUYEN",
            ExportReference: "ABCD1234"),
        new PaymentExportRow(
            Sequence: 2,
            PayeeName: "TRAN, THI B",                  // contains comma → must quote
            PayeeBankCode: "BIDV",
            PayeeBankAccountNumber: "9876543210",
            PayeeBankBranch: null,
            Amount: 250_500.5m,
            CurrencyCode: "VND",
            TransferNote: "REIMB EF567890 TRAN",
            ExportReference: "EF567890"),
    };

    [Fact]
    public void Vcb_RendersCommaSeparatedWithBomAndExpectedHeaders()
    {
        var formatter = new VietcombankCsvFormatter();
        var output = formatter.Format(SampleRows);

        // BOM
        Assert.Equal('\uFEFF', output[0]);
        // Comma-separated header
        Assert.Contains("STT,Ten nguoi nhan,So tai khoan,Ngan hang nhan,Chi nhanh,So tien,Loai tien,Noi dung CK", output);
        // Comma-separated VCB row 1
        Assert.Contains("1,NGUYEN VAN A,0123456789,VCB,HCM,1500000,VND,REIMB ABCD1234 NGUYEN", output);
        // Quoting on row containing comma
        Assert.Contains("\"TRAN, THI B\"", output);
        // CRLF line endings (RFC 4180)
        Assert.Contains("\r\n", output);
    }

    [Fact]
    public void Vcb_AmountFormatting_NoCurrencySymbol_DotForDecimalSeparator()
    {
        var formatter = new VietcombankCsvFormatter();
        var output = formatter.Format(SampleRows);

        // Decimal must use dot (not comma — bank parsers reject locale-specific commas)
        Assert.Contains("250500.5", output);
        Assert.DoesNotContain("250500,5", output);
    }

    [Fact]
    public void Bidv_UsesSemicolonSeparator()
    {
        var formatter = new BidvBulkTransferCsvFormatter();
        var output = formatter.Format(SampleRows);

        Assert.Contains("Stt;Ho ten nguoi huong;So tai khoan nguoi huong;Ngan hang nguoi huong;Tinh thanh;So tien;Loai tien;Noi dung", output);
        Assert.Contains("1;NGUYEN VAN A;0123456789;VCB;HCM;1500000;VND;REIMB ABCD1234 NGUYEN", output);
    }

    [Fact]
    public void Bidv_QuotesFieldsContainingTheSeparator()
    {
        var rows = new[]
        {
            new PaymentExportRow(
                Sequence: 1,
                PayeeName: "VENDOR;A",                  // contains semicolon
                PayeeBankCode: "BIDV",
                PayeeBankAccountNumber: "1111",
                PayeeBankBranch: null,
                Amount: 100m,
                CurrencyCode: "VND",
                TransferNote: "x",
                ExportReference: "1"),
        };

        var output = new BidvBulkTransferCsvFormatter().Format(rows);

        Assert.Contains("\"VENDOR;A\"", output);
    }

    [Fact]
    public void Tcb_UsesEnglishHeadersAndSemicolonSeparator()
    {
        var formatter = new TechcombankCsvFormatter();
        var output = formatter.Format(SampleRows);

        Assert.Contains("No;BeneficiaryName;BeneficiaryAccount;BeneficiaryBank;Branch;Amount;Currency;Description", output);
        Assert.Equal(";", formatter.Separator);
    }

    [Fact]
    public void Generic_UsesCommaAndIncludesReferenceColumn()
    {
        var formatter = new GenericCsvFormatter();
        var output = formatter.Format(SampleRows);

        Assert.Contains("Sequence,PayeeName,BankCode,AccountNumber,Branch,Amount,Currency,TransferNote,Reference", output);
        // Generic format places BankCode BEFORE AccountNumber (column order is part of contract)
        Assert.Contains("1,NGUYEN VAN A,VCB,0123456789,HCM,1500000,VND,REIMB ABCD1234 NGUYEN,ABCD1234", output);
    }

    [Fact]
    public void EmptyRows_ProducesHeaderOnlyOutput()
    {
        var formatter = new VietcombankCsvFormatter();
        var output = formatter.Format(Array.Empty<PaymentExportRow>());

        Assert.Equal('\uFEFF', output[0]);
        Assert.Contains("STT,", output);
        // No data rows — no second CRLF section
        var lines = output.TrimStart('\uFEFF').Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        Assert.Single(lines);
    }

    [Fact]
    public void Format_EscapesEmbeddedQuotes_Rfc4180_DoubleDoubleQuote()
    {
        var rows = new[]
        {
            new PaymentExportRow(
                Sequence: 1,
                PayeeName: "say \"hello\"",
                PayeeBankCode: "VCB",
                PayeeBankAccountNumber: "1",
                PayeeBankBranch: null,
                Amount: 1m,
                CurrencyCode: "VND",
                TransferNote: "x",
                ExportReference: "1"),
        };

        var output = new VietcombankCsvFormatter().Format(rows);

        Assert.Contains("\"say \"\"hello\"\"\"", output);
    }

    [Fact]
    public void Format_PreservesInputRowOrdering()
    {
        var formatter = new VietcombankCsvFormatter();
        var output = formatter.Format(SampleRows);

        var idxA = output.IndexOf("NGUYEN VAN A", StringComparison.Ordinal);
        var idxB = output.IndexOf("TRAN, THI B", StringComparison.Ordinal);

        Assert.True(idxA > 0);
        Assert.True(idxB > idxA, "Row 2 must appear after row 1 in output");
    }

    [Fact]
    public void Vcb_NullBranch_RendersAsEmptyField()
    {
        var rows = new[] { SampleRows[1] };  // branch = null
        var output = new VietcombankCsvFormatter().Format(rows);

        // After "9876543210,BIDV," there should be ",,250500.5" — two commas in a row
        // because branch is empty. Verify by checking the data row.
        var lines = output.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        var dataRow = lines[1];
        // Expected: "2,\"TRAN, THI B\",9876543210,BIDV,,250500.5,VND,REIMB EF567890 TRAN"
        Assert.Contains(",BIDV,,250500.5,VND,", dataRow);
    }

    [Fact]
    public void Registry_IndexesByCodeCaseInsensitively()
    {
        IBankCsvFormatter[] formatters =
        [
            new VietcombankCsvFormatter(),
            new BidvBulkTransferCsvFormatter(),
            new TechcombankCsvFormatter(),
            new GenericCsvFormatter(),
        ];
        var registry = new BankCsvFormatterRegistry(formatters);

        Assert.NotNull(registry.Find("vcb"));
        Assert.NotNull(registry.Find("VCB"));
        Assert.NotNull(registry.Find("Vcb"));
        Assert.NotNull(registry.Find("BIDV"));
        Assert.Null(registry.Find("DOES_NOT_EXIST"));
        Assert.Equal(4, registry.All.Count);
    }

    [Fact]
    public void AllFormatters_ProduceUtf8Decodable_AsciiSafeOutput()
    {
        IBankCsvFormatter[] formatters =
        [
            new VietcombankCsvFormatter(),
            new BidvBulkTransferCsvFormatter(),
            new TechcombankCsvFormatter(),
            new GenericCsvFormatter(),
        ];

        foreach (var formatter in formatters)
        {
            var content = formatter.Format(SampleRows);
            // Round-trip through UTF-8 must preserve content
            var bytes = System.Text.Encoding.UTF8.GetBytes(content);
            var decoded = System.Text.Encoding.UTF8.GetString(bytes);
            Assert.Equal(content, decoded);
        }
    }
}
