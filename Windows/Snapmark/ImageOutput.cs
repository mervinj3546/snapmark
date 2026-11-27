using System.Drawing.Imaging;

namespace Snapmark;

static class ImageOutput
{
    /// Puts the image on the clipboard as PNG (keeps quality in modern apps) and as a bitmap (for everything else).
    public static bool Copy(Bitmap bmp)
    {
        try
        {
            var data = new DataObject();
            var png = new MemoryStream();
            bmp.Save(png, ImageFormat.Png);
            data.SetData("PNG", false, png);
            data.SetData(DataFormats.Bitmap, true, bmp);
            Clipboard.SetDataObject(data, true, 10, 100);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// Saves to the Desktop as "Snapmark 2026-01-31 at 14.05.09.png".
    public static string? SaveToDesktop(Bitmap bmp)
    {
        try
        {
            var dir = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            var path = Path.Combine(dir, $"Snapmark {DateTime.Now:yyyy-MM-dd 'at' HH.mm.ss}.png");
            bmp.Save(path, ImageFormat.Png);
            return path;
        }
        catch
        {
            return null;
        }
    }
}
