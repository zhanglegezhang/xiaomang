namespace ShareX.ScreenCaptureLib
{
    internal static class BlurStrengthPresets
    {
        // Retain the nearest supported strength when reading older settings.
        internal static int Normalize(int value) => value <= 8 ? 3 : value <= 18 ? 13 : 23;
        internal static int Index(int value) => (Normalize(value) - 3) / 10;
        internal static int FromIndex(int index) => 3 + 10 * System.Math.Clamp(index, 0, 2);
        internal static string Label(int value) => (Index(value) + 1).ToString();
    }
}
