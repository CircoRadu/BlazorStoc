using OpenCvSharp;

namespace BlazorStoc.Services;

// A piece of the invoice's page picture around the region a value was read from, the region outlined: what the user compares the read value with.
public static class InvoiceRegionPicture
{
    public const double Margin = 8;

    // The page picture (PNG) cut to the box (points of the page) plus a margin; the outlines are drawn in points of the page too.
    // Returns a data: address for an img tag, or null when the picture or the box cannot be used.
    public static string? DataUri(byte[]? pagePng, InvoicePageData page, InvoiceBox area, IEnumerable<InvoiceBox> outlines, double marginX = Margin, double marginY = Margin)
    {
        if (pagePng is null || pagePng.Length == 0 || page.Width <= 0 || page.Height <= 0 || area.Width <= 0 || area.Height <= 0) return null;
        try
        {
            using var picture = Cv2.ImDecode(pagePng, ImreadModes.Color);
            if (picture.Empty()) return null;
            var scaleX = picture.Width / page.Width;
            var scaleY = picture.Height / page.Height;
            int Clamp(double value, int max) => (int)Math.Round(Math.Clamp(value, 0, max));
            var left = Clamp((area.X - marginX) * scaleX, picture.Width - 1);
            var top = Clamp((area.Y - marginY) * scaleY, picture.Height - 1);
            var right = Clamp((area.Right + marginX) * scaleX, picture.Width);
            var bottom = Clamp((area.Bottom + marginY) * scaleY, picture.Height);
            if (right - left < 4 || bottom - top < 4) return null;
            foreach (var outline in outlines)
            {
                var a = new Point((int)Math.Round(outline.X * scaleX), (int)Math.Round(outline.Y * scaleY));
                var b = new Point((int)Math.Round(outline.Right * scaleX), (int)Math.Round(outline.Bottom * scaleY));
                Cv2.Rectangle(picture, a, b, new Scalar(40, 90, 220), 2);   // BGR: a warm red
            }
            using var crop = new Mat(picture, new Rect(left, top, right - left, bottom - top));
            return "data:image/png;base64," + Convert.ToBase64String(crop.ImEncode(".png"));
        }
        catch (Exception exception) when (exception is OpenCVException or ArgumentException or OutOfMemoryException) { return null; }
    }
}
