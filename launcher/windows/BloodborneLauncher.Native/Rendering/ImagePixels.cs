namespace BloodborneLauncher.Native.Rendering;
internal static class ImagePixels
{
    // Linear channel weighting commutes with premultiplication; preserve alpha exactly.
    public static void Grayscale(Span<byte> bgra)
    {
        if (bgra.Length % 4 != 0) throw new ArgumentException("Expected complete BGRA pixels.");
        for (int at = 0; at < bgra.Length; at += 4)
        {
            byte gray = (byte)((19 * bgra[at] + 183 * bgra[at + 1] + 54 * bgra[at + 2] + 128) >> 8);
            bgra[at] = bgra[at + 1] = bgra[at + 2] = gray;
        }
    }
}
