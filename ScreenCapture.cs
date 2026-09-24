using System;
using System.Runtime.InteropServices;

namespace UtiCdHelper;

public static class ScreenCapture
{
    private const int SrcCopy = 0x00CC0020;
    private const uint DibRgbColors = 0;

    public static GrayImage Capture(int x, int y, int width, int height)
    {
        if (width <= 0 || height <= 0)
        {
            throw new ArgumentException("截取区域必须大于 0。");
        }

        IntPtr screenDc = GetDC(IntPtr.Zero);
        IntPtr memDc = CreateCompatibleDC(screenDc);
        IntPtr bitmap = CreateCompatibleBitmap(screenDc, width, height);
        IntPtr previous = SelectObject(memDc, bitmap);

        try
        {
            BitBlt(memDc, 0, 0, width, height, screenDc, x, y, SrcCopy);

            var info = new BITMAPINFO
            {
                bmiHeader = new BITMAPINFOHEADER
                {
                    biSize = (uint)Marshal.SizeOf<BITMAPINFOHEADER>(),
                    biWidth = width,
                    biHeight = -height,
                    biPlanes = 1,
                    biBitCount = 32,
                    biCompression = 0,
                },
                bmiColors = new uint[3],
            };

            int stride = width * 4;
            byte[] buffer = new byte[stride * height];
            GetDIBits(memDc, bitmap, 0, (uint)height, buffer, ref info, DibRgbColors);

            return ToGray(buffer, width, height, stride);
        }
        finally
        {
            SelectObject(memDc, previous);
            DeleteObject(bitmap);
            DeleteDC(memDc);
            ReleaseDC(IntPtr.Zero, screenDc);
        }
    }

    private static GrayImage ToGray(byte[] buffer, int width, int height, int stride)
    {
        byte[] gray = new byte[width * height];

        for (int row = 0; row < height; row++)
        {
            int source = row * stride;
            int target = row * width;

            for (int column = 0; column < width; column++)
            {
                int offset = source + (column * 4);
                byte blue = buffer[offset];
                byte green = buffer[offset + 1];
                byte red = buffer[offset + 2];

                gray[target + column] = (byte)(((red * 299) + (green * 587) + (blue * 114)) / 1000);
            }
        }

        return new GrayImage(gray, width, height);
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDc);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateCompatibleDC(IntPtr hDc);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteDC(IntPtr hDc);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateCompatibleBitmap(IntPtr hDc, int width, int height);

    [DllImport("gdi32.dll")]
    private static extern IntPtr SelectObject(IntPtr hDc, IntPtr hObject);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr hObject);

    [DllImport("gdi32.dll")]
    private static extern bool BitBlt(IntPtr hdcDest, int xDest, int yDest, int width, int height, IntPtr hdcSrc, int xSrc, int ySrc, int dwRop);

    [DllImport("gdi32.dll")]
    private static extern int GetDIBits(IntPtr hDc, IntPtr hBitmap, uint uStartScan, uint cScanLines, byte[] lpvBits, ref BITMAPINFO lpbmi, uint uUsage);

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFOHEADER
    {
        public uint biSize;
        public int biWidth;
        public int biHeight;
        public ushort biPlanes;
        public ushort biBitCount;
        public uint biCompression;
        public uint biSizeImage;
        public int biXPelsPerMeter;
        public int biYPelsPerMeter;
        public uint biClrUsed;
        public uint biClrImportant;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFO
    {
        public BITMAPINFOHEADER bmiHeader;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 3)]
        public uint[] bmiColors;
    }
}

public sealed class GrayImage
{
    public GrayImage(byte[] data, int width, int height)
    {
        Data = data;
        Width = width;
        Height = height;
    }

    public byte[] Data { get; }

    public int Width { get; }

    public int Height { get; }
}
