namespace RoImportBridge;

internal static class AppAssets
{
    public static Icon GetIcon()
    {
        using var stream = typeof(AppAssets).Assembly.GetManifestResourceStream("RoImportBridge.logo.ico");

        if (stream is null)
        {
            return (Icon)SystemIcons.Application.Clone();
        }

        using var icon = new Icon(stream);
        return (Icon)icon.Clone();
    }

    public static Image? GetLogo()
    {
        using var stream = typeof(AppAssets).Assembly.GetManifestResourceStream("RoImportBridge.logo.png");

        if (stream is null)
        {
            return null;
        }

        using var image = Image.FromStream(stream);
        return new Bitmap(image);
    }
}
