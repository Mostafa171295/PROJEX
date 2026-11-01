using Projex.Application.Reporting;

namespace Projex.Domain.Tests;

public sealed class CsvWriterTests
{
    [Fact]
    public void Quotes_commas_quotes_and_line_breaks()
    {
        Assert.Equal("a", CsvWriter.Cell("a"));
        Assert.Equal("\"a,b\"", CsvWriter.Cell("a,b"));
        Assert.Equal("\"say \"\"hi\"\"\"", CsvWriter.Cell("say \"hi\""));
        Assert.Equal("\"x\ny\"", CsvWriter.Cell("x\ny"));
        Assert.Equal(string.Empty, CsvWriter.Cell(null));
    }

    [Theory]
    [InlineData("=1+1", "'=1+1")]
    [InlineData("@SUM(A1)", "'@SUM(A1)")]
    [InlineData("-cmd|' /C calc'!A0", "'-cmd|' /C calc'!A0")]
    [InlineData("+HYPERLINK(\"x\")", "\"'+HYPERLINK(\"\"x\"\")\"")]
    public void Neutralises_formula_injection(string input, string expected) => Assert.Equal(expected, CsvWriter.Cell(input));

    [Theory]
    [InlineData("-100.5")]
    [InlineData("1234.5678")]
    public void Leaves_plain_numbers_alone(string input) => Assert.Equal(input, CsvWriter.Cell(input));

    [Fact]
    public void Writes_header_and_rows_with_crlf()
    {
        var text = CsvWriter.Write(new[] { "Code", "Amount" }, new[] { (IReadOnlyList<string?>)new string?[] { "A,1", CsvWriter.Num(10.5m) } });
        Assert.Equal("Code,Amount\r\n\"A,1\",10.5\r\n", text);
    }

    [Fact]
    public void Rejects_ragged_rows()
        => Assert.Throws<ArgumentException>(() => CsvWriter.Write(new[] { "A", "B" }, new[] { (IReadOnlyList<string?>)new string?[] { "only-one" } }));
}
