using System;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace UtiCdHelper;

public static class ImagePreprocessor
{
    // Tesseract 对贴着边缘的字符识别率明显下降，二值化后统一补一圈背景色留白
    private const int BorderPadding = 10;

    public static GrayImage Apply(GrayImage source, int scale, int threshold, bool invert)
    {
        int factor = Math.Clamp(scale, 1, 8);
        GrayImage scaled = factor == 1 ? source : Resize(source, factor);
        byte[] data = Binarize(scaled, threshold, invert);

        return AddBorder(new GrayImage(data, scaled.Width, scaled.Height), BorderPadding);
    }

    private static GrayImage AddBorder(GrayImage image, int padding)
    {
        // 左上角像素几乎必然是背景，用它当留白色
        byte border = image.Data[0];

        int width = image.Width + (padding * 2);
        int height = image.Height + (padding * 2);
        byte[] data = new byte[width * height];
        Array.Fill(data, border);

        for (int y = 0; y < image.Height; y++)
        {
            Array.Copy(image.Data, y * image.Width, data, ((y + padding) * width) + padding, image.Width);
        }

        return new GrayImage(data, width, height);
    }

    private static GrayImage Resize(GrayImage source, int factor)
    {
        int width = source.Width * factor;
        int height = source.Height * factor;
        byte[] data = new byte[width * height];
        float ratio = 1f / factor;

        for (int y = 0; y < height; y++)
        {
            float sourceY = y * ratio;

            for (int x = 0; x < width; x++)
            {
                data[(y * width) + x] = (byte)Sample(source, x * ratio, sourceY);
            }
        }

        return new GrayImage(data, width, height);
    }

    private static float Sample(GrayImage image, float x, float y)
    {
        int x0 = (int)x;
        int y0 = (int)y;
        int x1 = Math.Min(image.Width - 1, x0 + 1);
        int y1 = Math.Min(image.Height - 1, y0 + 1);
        float dx = x - x0;
        float dy = y - y0;

        float top = (image.Data[(y0 * image.Width) + x0] * (1 - dx)) + (image.Data[(y0 * image.Width) + x1] * dx);
        float bottom = (image.Data[(y1 * image.Width) + x0] * (1 - dx)) + (image.Data[(y1 * image.Width) + x1] * dx);

        return (top * (1 - dy)) + (bottom * dy);
    }

    private static byte[] Binarize(GrayImage image, int threshold, bool invert)
    {
        byte[] result = new byte[image.Data.Length];
        byte background = EstimateBackground(image);

        int value = threshold >= 0 ? threshold : Otsu(image);
        bool backgroundIsBright = background >= value;

        for (int i = 0; i < image.Data.Length; i++)
        {
            byte gray = image.Data[i];
            bool isText = backgroundIsBright ? gray < value : gray >= value;

            if (invert)
            {
                isText = !isText;
            }

            result[i] = isText ? (byte)0 : (byte)255;
        }

        return result;
    }

    private static byte EstimateBackground(GrayImage image)
    {
        int[] histogram = new int[256];

        foreach (byte value in image.Data)
        {
            histogram[value]++;
        }

        int bestIndex = 0;
        for (int i = 1; i < histogram.Length; i++)
        {
            if (histogram[i] > histogram[bestIndex])
            {
                bestIndex = i;
            }
        }

        return (byte)bestIndex;
    }

    private static int Otsu(GrayImage image)
    {
        int[] histogram = new int[256];

        foreach (byte value in image.Data)
        {
            histogram[value]++;
        }

        int total = image.Data.Length;
        float sum = 0;
        for (int i = 0; i < 256; i++)
        {
            sum += i * histogram[i];
        }

        float sumB = 0;
        int weightB = 0;
        float maxVariance = 0;
        int best = 127;

        for (int i = 0; i < 256; i++)
        {
            weightB += histogram[i];
            if (weightB == 0)
            {
                continue;
            }

            int weightF = total - weightB;
            if (weightF == 0)
            {
                break;
            }

            sumB += i * histogram[i];
            float meanB = sumB / weightB;
            float meanF = (sum - sumB) / weightF;
            float variance = (weightB * weightF) * ((meanB - meanF) * (meanB - meanF));

            if (variance > maxVariance)
            {
                maxVariance = variance;
                best = i;
            }
        }

        return best;
    }

    public static byte[] ToPng(GrayImage image)
    {
        BitmapSource source = BitmapSource.Create(
            image.Width,
            image.Height,
            96,
            96,
            PixelFormats.Gray8,
            null,
            image.Data,
            image.Width);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(source));

        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }
}
