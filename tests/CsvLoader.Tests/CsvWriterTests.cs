using System.Text;
using Shouldly;

namespace CsvLoader.Tests;

/// <summary>
/// Tests for CSV format requirements FR-15 through FR-18 and FR-21.
/// These tests run against the ReferenceCsvWriter (which implements the spec)
/// and serve as the acceptance bar for Han's production CsvWriter.
/// </summary>
public sealed class CsvWriterTests
{
    private readonly ICsvWriter _writer = new ReferenceCsvWriter();

    // -----------------------------------------------------------------------
    // FR-15: Header row
    // -----------------------------------------------------------------------

    [Fact]
    public void FR15_HeaderRow_IsFirstLine_UsingColumnNames()
    {
        var csv = _writer.WriteCsv(["OrderId", "Status", "Amount"], []);

        var firstLine = csv.Split('\n', StringSplitOptions.RemoveEmptyEntries)[0].TrimEnd('\r');
        firstLine.ShouldBe("OrderId;Status;Amount");
    }

    [Fact]
    public void FR15_HeaderRow_ContainsAllColumns_InOrder()
    {
        var columns = new[] { "A", "B", "C", "D" };
        var csv = _writer.WriteCsv(columns, []);

        var firstLine = NormalizeLineEndings(csv).Split('\n')[0];
        firstLine.ShouldBe("A;B;C;D");
    }

    // -----------------------------------------------------------------------
    // FR-16: Semicolon delimiter
    // -----------------------------------------------------------------------

    [Fact]
    public void FR16_DataRow_FieldsSeparatedBySemicolon()
    {
        var csv = _writer.WriteCsv(
            ["Col1", "Col2", "Col3"],
            [["Alpha", "Beta", "Gamma"]]);

        var lines = NormalizeLineEndings(csv).Split('\n', StringSplitOptions.RemoveEmptyEntries);
        lines[1].ShouldBe("Alpha;Beta;Gamma");
    }

    [Fact]
    public void FR16_MultipleDataRows_AllUseSemicolonDelimiter()
    {
        var csv = _writer.WriteCsv(
            ["Id", "Name"],
            [["1", "Alice"], ["2", "Bob"], ["3", "Carol"]]);

        var lines = NormalizeLineEndings(csv).Split('\n', StringSplitOptions.RemoveEmptyEntries);
        lines.Count().ShouldBe(4); // 1 header + 3 data
        lines[1].ShouldBe("1;Alice");
        lines[2].ShouldBe("2;Bob");
        lines[3].ShouldBe("3;Carol");
    }

    // -----------------------------------------------------------------------
    // FR-17: UTF-8 encoding
    // -----------------------------------------------------------------------

    [Fact]
    public void FR17_Output_IsEncodedAsUtf8_WithoutBom()
    {
        var bytes = _writer.WriteCsvUtf8(["Feld"], [["Wert"]]);

        // UTF-8 without BOM: first bytes are NOT EF BB BF
        if (bytes.Length >= 3)
        {
            (bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
                .ShouldBeFalse("output must be UTF-8 without BOM (FR-17)");
        }
    }

    [Fact]
    public void FR17_UnicodeCharacters_SurvivedRoundTrip()
    {
        var bytes = _writer.WriteCsvUtf8(["Bezeichnung"], [["Österreich — Müller"]]);

        var decoded = Encoding.UTF8.GetString(bytes);
        decoded.ShouldContain("Österreich — Müller");
    }

    // -----------------------------------------------------------------------
    // FR-18: Quoting rules
    // -----------------------------------------------------------------------

    [Fact]
    public void FR18_FieldWithSemicolon_IsQuoted()
    {
        var csv = _writer.WriteCsv(["Value"], [["Hello;World"]]);

        var dataLine = GetDataLine(csv, 1);
        dataLine.ShouldBe("\"Hello;World\"");
    }

    [Fact]
    public void FR18_FieldWithDoubleQuote_IsQuotedAndEscaped()
    {
        var csv = _writer.WriteCsv(["Value"], [["Say \"hello\""]]);

        var dataLine = GetDataLine(csv, 1);
        dataLine.ShouldBe("\"Say \"\"hello\"\"\"");
    }

    [Fact]
    public void FR18_FieldWithNewline_IsQuoted()
    {
        var csv = _writer.WriteCsv(["Value"], [["Line1\nLine2"]]);

        var dataLine = GetDataLine(csv, 1);
        dataLine.ShouldStartWith("\"");
        dataLine.ShouldEndWith("\"");
        dataLine.ShouldContain("Line1\nLine2");
    }

    [Fact]
    public void FR18_FieldWithCarriageReturnNewline_IsQuoted()
    {
        var csv = _writer.WriteCsv(["Value"], [["Line1\r\nLine2"]]);

        var dataLine = GetDataLine(csv, 1);
        dataLine.ShouldStartWith("\"");
    }

    [Fact]
    public void FR18_NormalField_IsNOT_Quoted()
    {
        var csv = _writer.WriteCsv(["Value"], [["PlainText"]]);

        var dataLine = GetDataLine(csv, 1);
        dataLine.ShouldBe("PlainText");
    }

    [Fact]
    public void FR18_NullField_ProducesEmptyUnquotedValue()
    {
        var csv = _writer.WriteCsv(["A", "B"], [[null, "X"]]);

        var dataLine = GetDataLine(csv, 1);
        dataLine.ShouldBe(";X");
    }

    [Fact]
    public void FR18_ColumnNameWithSemicolon_IsQuotedInHeader()
    {
        var csv = _writer.WriteCsv(["Col;Name"], []);

        var firstLine = NormalizeLineEndings(csv).Split('\n')[0];
        firstLine.ShouldBe("\"Col;Name\"");
    }

    // -----------------------------------------------------------------------
    // FR-21: Empty result set → header-only CSV, no data rows, exit 0
    // -----------------------------------------------------------------------

    [Fact]
    public void FR21_EmptyResultSet_ProducesHeaderOnlyOutput()
    {
        var csv = _writer.WriteCsv(["Id", "Name", "Value"], []);

        var lines = NormalizeLineEndings(csv).Split('\n', StringSplitOptions.RemoveEmptyEntries);
        lines.Count().ShouldBe(1, "only the header row should be present");
        lines[0].ShouldBe("Id;Name;Value");
    }

    [Fact]
    public void FR21_EmptyResultSet_OutputIsNotNullOrEmpty()
    {
        var csv = _writer.WriteCsv(["Col1"], []);

        csv.ShouldNotBeNullOrEmpty("an empty result must still produce a header");
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static string NormalizeLineEndings(string s) => s.Replace("\r\n", "\n");

    private static string GetDataLine(string csv, int index)
    {
        // Split on newlines but keep quoted newlines intact by working on the raw string
        var lines = new List<string>();
        var start = 0;
        var inQuote = false;
        for (var i = 0; i < csv.Length; i++)
        {
            if (csv[i] == '"') inQuote = !inQuote;
            if (!inQuote && (csv[i] == '\n'))
            {
                var line = csv[start..i].TrimEnd('\r');
                if (line.Length > 0) lines.Add(line);
                start = i + 1;
            }
        }
        if (start < csv.Length)
        {
            var last = csv[start..].TrimEnd('\r', '\n');
            if (last.Length > 0) lines.Add(last);
        }
        return lines[index];
    }
}
