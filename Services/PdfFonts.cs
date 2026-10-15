using PdfSharp.Fonts;

namespace BlazorStoc.Services;

// Embeds PT Sans (SIL Open Font License, Assets/Fonts) so a PDF never depends on fonts installed on the server and renders Romanian diacritics correctly.
// Shared by every PDF writer of the application (inventory, consumption note, project situation): the writers of a module and those of the core use the same face.
public static class PdfFonts
{
    public const string FamilyName = "PT Sans";

    // Static field initializers of a writer run in declaration order and are guaranteed to run before its XFont fields are initialized:
    // each writer calls this first, so the resolver is in place by then.
    public static bool EnsureResolver()
    {
        if (GlobalFontSettings.FontResolver is not PtSansResolver) GlobalFontSettings.FontResolver = new PtSansResolver();
        return true;
    }

    private sealed class PtSansResolver : IFontResolver
    {
        private const string RegularFace = "PTSansRegular";
        private const string BoldFace = "PTSansBold";
        private static readonly Lazy<byte[]> Regular = new(() => ReadResource("PTSans-Regular.ttf"));
        private static readonly Lazy<byte[]> Bold = new(() => ReadResource("PTSans-Bold.ttf"));

        public byte[] GetFont(string faceName) => faceName switch
        {
            RegularFace => Regular.Value,
            BoldFace => Bold.Value,
            _ => throw new InvalidOperationException($"Font necunoscut: {faceName}")
        };

        public FontResolverInfo? ResolveTypeface(string familyName, bool isBold, bool isItalic) =>
            string.Equals(familyName, FamilyName, StringComparison.OrdinalIgnoreCase)
                ? new FontResolverInfo(isBold ? BoldFace : RegularFace)
                : null;

        private static byte[] ReadResource(string fileName)
        {
            var assembly = typeof(PtSansResolver).Assembly;
            var name = $"{assembly.GetName().Name}.Assets.Fonts.{fileName}";
            using var resourceStream = assembly.GetManifestResourceStream(name)
                ?? throw new InvalidOperationException($"Fontul „{fileName}” lipsește din resursele aplicației.");
            using var memory = new MemoryStream();
            resourceStream.CopyTo(memory);
            return memory.ToArray();
        }
    }
}
