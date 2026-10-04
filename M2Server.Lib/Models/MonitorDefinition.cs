namespace M2Server.Lib.Models;

public class MonitorDefinition
{
    public string DeviceName { get; set; } = string.Empty;

    public string DevicePath { get; set; } = string.Empty;

    public int PpiX { get; set; }

    public int PpiY { get; set; }

    public int DisplayNumber { get; set; }

    public string UniqueId { get; set; } = string.Empty;

    public int Width { get; set; }

    public int Height { get; set; }

    public bool IsPrimary { get; set; }
}