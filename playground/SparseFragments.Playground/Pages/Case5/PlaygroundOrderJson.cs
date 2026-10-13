using System.Globalization;
using System.Text;
using System.Text.Json;
using SparseFragments.Playground.Models;

namespace SparseFragments.Playground.Pages.Case5;

internal static class PlaygroundOrderJson
{
    internal static string WriteDto(PlaygroundOrderDto dto)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
        {
            writer.WriteStartObject();
            writer.WriteString("Customer", dto.Customer);
            writer.WriteString("Notes", dto.Notes);
            writer.WriteNumber("Total", dto.Total);
            writer.WriteStartArray("Lines");
            foreach (var line in dto.Lines)
            {
                WriteLine(writer, line);
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    internal static string WriteEntity(PlaygroundOrderEntity order)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
        {
            writer.WriteStartObject();
            writer.WriteNumber("Id", order.Id);
            writer.WriteNumber("Revision", order.Revision);
            writer.WriteString("CustomerName", order.CustomerName);
            if (order.Note is null)
            {
                writer.WriteNull("Note");
            }
            else
            {
                writer.WriteStartObject("Note");
                writer.WriteNumber("Id", order.Note.Id);
                writer.WriteString("Text", order.Note.Text);
                writer.WriteEndObject();
            }

            writer.WriteNumber("Total", order.Total);
            writer.WriteStartArray("Lines");
            foreach (var line in order.Lines.OrderBy(static item => item.SortOrder))
            {
                writer.WriteStartObject();
                writer.WriteNumber("Id", line.Id);
                writer.WriteString("Product", line.Product);
                writer.WriteNumber("Qty", line.Qty);
                writer.WriteNumber("SortOrder", line.SortOrder);
                writer.WriteNumber("OrderId", line.OrderId);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    internal static string FormatValue(object? value) =>
        value switch
        {
            null => "null",
            string text => text,
            PlaygroundOrderLineDto line => WriteLine(line),
            System.Collections.IEnumerable sequence => "["
                + string.Join(
                    ", ",
                    sequence.Cast<object?>().Select(static item => FormatValue(item))
                )
                + "]",
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString() ?? "null",
        };

    private static void WriteLine(Utf8JsonWriter writer, PlaygroundOrderLineDto line)
    {
        writer.WriteStartObject();
        if (line.Id.HasValue)
        {
            writer.WriteNumber("Id", line.Id.Value);
        }
        else
        {
            writer.WriteNull("Id");
        }

        writer.WriteString("Product", line.Product);
        writer.WriteNumber("Qty", line.Qty);
        if (line.TemporaryId.HasValue)
        {
            writer.WriteString("TemporaryId", line.TemporaryId.Value);
        }

        writer.WriteEndObject();
    }

    private static string WriteLine(PlaygroundOrderLineDto line)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            WriteLine(writer, line);
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }
}
