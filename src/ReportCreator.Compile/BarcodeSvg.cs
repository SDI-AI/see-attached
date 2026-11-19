using System.Globalization;
using System.Net;
using System.Text;
using ReportCreator.Model;
using ZXing;
using ZXing.Common;
using ZXing.QrCode.Internal;

namespace ReportCreator.Compile;

internal readonly record struct BarcodeDrawing(bool Ok, string Svg, string? Caption, string? Problem)
{
    public static BarcodeDrawing Invalid(string problem) => new(false, "", null, problem);

    public static BarcodeDrawing Ready(string svg, string? caption) => new(true, svg, caption, null);
}

internal readonly record struct BarcodeBox(decimal Width, decimal Height);

// ZXing.Net builds the module matrix. Its SVG writer emits an XML declaration
// and a doctype, which do not belong in the report HTML. This writes a small
// inline SVG that scales to the block's millimetres.
internal static class BarcodeSvg
{
    public static BarcodeBox Box(BarcodeBlock barcode)
    {
        var square = barcode.Symbology is BarcodeSymbology.QrCode or BarcodeSymbology.DataMatrix;
        if (square)
        {
            if (barcode.WidthMm is null && barcode.HeightMm is null)
                return new BarcodeBox(32m, 32m);
            if (barcode.WidthMm is null)
                return new BarcodeBox(barcode.HeightMm!.Value, barcode.HeightMm.Value);
            if (barcode.HeightMm is null)
                return new BarcodeBox(barcode.WidthMm.Value, barcode.WidthMm.Value);
            return new BarcodeBox(barcode.WidthMm.Value, barcode.HeightMm.Value);
        }

        if (barcode.Symbology == BarcodeSymbology.Pdf417)
            return new BarcodeBox(barcode.WidthMm ?? 64m, barcode.HeightMm ?? 20m);

        return new BarcodeBox(barcode.WidthMm ?? 50m, barcode.HeightMm ?? 18m);
    }

    public static BarcodeDrawing Draw(BarcodeBlock barcode, string contents)
    {
        if (string.IsNullOrEmpty(contents))
            return BarcodeDrawing.Invalid("has no value");
        if (contents.Length > BarcodeBlock.MaxValueLength)
            return BarcodeDrawing.Invalid("value is too long");
        if (!Enum.IsDefined(barcode.Symbology))
            return BarcodeDrawing.Invalid("could not be encoded");

        var caption = contents;
        switch (barcode.Symbology)
        {
            case BarcodeSymbology.Ean13:
                if (!TryCanonical(contents, 12, out caption))
                    return BarcodeDrawing.Invalid("is not a valid EAN-13 value");
                break;
            case BarcodeSymbology.UpcA:
                if (!TryCanonical(contents, 11, out caption))
                    return BarcodeDrawing.Invalid("is not a valid UPC-A value");
                break;
            case BarcodeSymbology.Code39 when !IsCode39(contents):
                return BarcodeDrawing.Invalid("is not a valid Code 39 value");
        }

        try
        {
            var quiet = barcode.QuietZone ?? DefaultQuiet(barcode.Symbology);
            if (quiet < 0)
                quiet = 0;
            else if (quiet > BarcodeBlock.MaxQuietZone)
                quiet = BarcodeBlock.MaxQuietZone;

            var hints = new Dictionary<EncodeHintType, object>
            {
                [EncodeHintType.MARGIN] = quiet
            };
            if (barcode.Symbology == BarcodeSymbology.QrCode)
            {
                hints[EncodeHintType.ERROR_CORRECTION] = Level(barcode.ErrorCorrection);
                hints[EncodeHintType.CHARACTER_SET] = "UTF-8";
            }
            else if (barcode.Symbology is BarcodeSymbology.DataMatrix or BarcodeSymbology.Pdf417)
            {
                hints[EncodeHintType.CHARACTER_SET] = "UTF-8";
            }

            var matrix = new MultiFormatWriter().encode(contents, FormatOf(barcode.Symbology), 0, 0, hints);
            if (matrix is null || matrix.Width < 1 || matrix.Height < 1)
                return BarcodeDrawing.Invalid("could not be encoded");

            var box = Box(barcode);
            var showText = barcode.ShowText && IsLinear(barcode.Symbology);
            return BarcodeDrawing.Ready(ToSvg(matrix, box, barcode, caption), showText ? caption : null);
        }
        catch (Exception)
        {
            // A value the encoder rejects is a blank symbol, not a failed compile.
            return BarcodeDrawing.Invalid("could not be encoded");
        }
    }

    private static string ToSvg(BitMatrix matrix, BarcodeBox box, BarcodeBlock barcode, string labelText)
    {
        var foreground = StyleRules.IsHexColor(barcode.Color) ? barcode.Color! : "#111827";
        var background = StyleRules.IsHexColor(barcode.Background) ? barcode.Background! : "#ffffff";
        var label = NameOf(barcode.Symbology);
        if (labelText.Length > 0 && labelText.Length <= 80)
            label = label + " " + labelText;

        var svg = new StringBuilder();
        svg.Append("<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 ")
            .Append(Invariant(matrix.Width)).Append(' ').Append(Invariant(matrix.Height))
            .Append("\" width=\"").Append(Num(box.Width)).Append("mm\" height=\"").Append(Num(box.Height))
            .Append("mm\" preserveAspectRatio=\"none\" shape-rendering=\"crispEdges\" role=\"img\" aria-label=\"")
            .Append(WebUtility.HtmlEncode(label))
            .Append('"');
        if (barcode.Align == TextAlign.Center)
            svg.Append(" style=\"margin-left:auto;margin-right:auto\"");
        else if (barcode.Align == TextAlign.Right)
            svg.Append(" style=\"margin-left:auto\"");
        svg.Append("><rect width=\"100%\" height=\"100%\" fill=\"").Append(background).Append("\"/>");
        svg.Append("<g fill=\"").Append(foreground).Append("\">");
        for (var y = 0; y < matrix.Height; y++)
        {
            var x = 0;
            while (x < matrix.Width)
            {
                if (!matrix[x, y])
                {
                    x++;
                    continue;
                }

                var start = x;
                while (x < matrix.Width && matrix[x, y])
                    x++;
                svg.Append("<rect x=\"").Append(Invariant(start))
                    .Append("\" y=\"").Append(Invariant(y))
                    .Append("\" width=\"").Append(Invariant(x - start))
                    .Append("\" height=\"1\"/>");
            }
        }

        svg.Append("</g></svg>");
        return svg.ToString();
    }

    private static bool TryCanonical(string value, int shortLength, out string canonical)
    {
        canonical = value;
        if (value.Length != shortLength && value.Length != shortLength + 1)
            return false;
        if (!AllDigits(value))
            return false;

        var body = value[..shortLength];
        var check = CheckDigit(body);
        if (value.Length == shortLength + 1)
            return value[shortLength] == (char)('0' + check);

        canonical = body + (char)('0' + check);
        return true;
    }

    private static int CheckDigit(string body)
    {
        var sum = 0;
        for (var index = 0; index < body.Length; index++)
            sum += (body[index] - '0') * (index % 2 == 0 ? 1 : 3);
        return (10 - sum % 10) % 10;
    }

    private static bool AllDigits(string value)
    {
        foreach (var character in value)
        {
            if (!char.IsAsciiDigit(character))
                return false;
        }

        return true;
    }

    private static bool IsCode39(string value)
    {
        foreach (var character in value)
        {
            if (character is not (>= '0' and <= '9' or >= 'A' and <= 'Z' or ' ' or '-' or '.' or '$' or '/' or '+' or '%'))
                return false;
        }

        return true;
    }

    private static bool IsLinear(BarcodeSymbology symbology) =>
        symbology is BarcodeSymbology.Code128 or BarcodeSymbology.Code39 or BarcodeSymbology.Ean13 or BarcodeSymbology.UpcA;

    private static int DefaultQuiet(BarcodeSymbology symbology) => symbology switch
    {
        BarcodeSymbology.QrCode => 4,
        BarcodeSymbology.DataMatrix => 1,
        BarcodeSymbology.Pdf417 => 2,
        _ => 10
    };

    private static ErrorCorrectionLevel Level(QrErrorCorrection level) => level switch
    {
        QrErrorCorrection.Low => ErrorCorrectionLevel.L,
        QrErrorCorrection.Quartile => ErrorCorrectionLevel.Q,
        QrErrorCorrection.High => ErrorCorrectionLevel.H,
        _ => ErrorCorrectionLevel.M
    };

    private static BarcodeFormat FormatOf(BarcodeSymbology symbology) => symbology switch
    {
        BarcodeSymbology.Code39 => BarcodeFormat.CODE_39,
        BarcodeSymbology.QrCode => BarcodeFormat.QR_CODE,
        BarcodeSymbology.Ean13 => BarcodeFormat.EAN_13,
        BarcodeSymbology.UpcA => BarcodeFormat.UPC_A,
        BarcodeSymbology.DataMatrix => BarcodeFormat.DATA_MATRIX,
        BarcodeSymbology.Pdf417 => BarcodeFormat.PDF_417,
        _ => BarcodeFormat.CODE_128
    };

    private static string NameOf(BarcodeSymbology symbology) => symbology switch
    {
        BarcodeSymbology.Code39 => "Code 39",
        BarcodeSymbology.QrCode => "QR Code",
        BarcodeSymbology.Ean13 => "EAN-13",
        BarcodeSymbology.UpcA => "UPC-A",
        BarcodeSymbology.DataMatrix => "Data Matrix",
        BarcodeSymbology.Pdf417 => "PDF417",
        _ => "Code 128"
    };

    private static string Invariant(int value) => value.ToString(CultureInfo.InvariantCulture);

    private static string Num(decimal value) => value.ToString("0.##", CultureInfo.InvariantCulture);
}
