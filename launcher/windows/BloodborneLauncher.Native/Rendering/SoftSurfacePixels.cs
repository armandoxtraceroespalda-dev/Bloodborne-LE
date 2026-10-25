namespace BloodborneLauncher.Native.Rendering;

internal static class SoftSurfacePixels
{
    public const int Size = 64;
    public static byte[] Create(bool black, bool dialog)
    {
        var bytes = new byte[Size * Size * 4];
        for (int y = 0; y < Size; y++)
            for (int x = 0; x < Size; x++)
            {
                double ax = Math.Min(1, Math.Min(x, Size - 1 - x) / 16.0);
                double ay = Math.Min(1, Math.Min(y, Size - 1 - y) / 16.0);
                byte alpha = (byte)Math.Round((dialog ? 255 : 210 * .85 * (black ? .5625 : 1)) * ax * ay);
                int at = (y * Size + x) * 4;
                bytes[at] = (byte)Math.Round((black ? 0 : 23) * alpha / 255.0);
                bytes[at + 1] = (byte)Math.Round((black ? 0 : 22) * alpha / 255.0);
                bytes[at + 2] = (byte)Math.Round((black ? 0 : 20) * alpha / 255.0);
                bytes[at + 3] = alpha;
            }
        return bytes;
    }
}
