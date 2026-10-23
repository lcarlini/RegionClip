namespace RegionClip;

static class Program
{
    static readonly byte[]? IconBytes = LoadIconBytes();

    [STAThread]
    static void Main()
    {
        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm());
    }

    internal static Icon? CreateIcon()
    {
        if (IconBytes is null)
            return null;

        return new Icon(new MemoryStream(IconBytes));
    }

    static byte[]? LoadIconBytes()
    {
        using var stream = typeof(Program).Assembly.GetManifestResourceStream("RegionClip.RegionClip.ico");
        if (stream is null)
            return null;

        var bytes = new byte[stream.Length];
        stream.ReadExactly(bytes);
        return bytes;
    }
}
