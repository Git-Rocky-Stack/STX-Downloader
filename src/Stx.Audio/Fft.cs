namespace Stx.Audio;

/// <summary>A small radix-2 FFT, so measuring needs no extra dependency.</summary>
public static class Fft
{
    public static void Transform(double[] re, double[] im)
    {
        int n = re.Length;
        if (n == 0 || (n & (n - 1)) != 0 || im.Length != n) throw new ArgumentException("Needs matching arrays with a power-of-two length.");

        for (int i = 1, j = 0; i < n; i++)
        {
            int bit = n >> 1;
            for (; (j & bit) != 0; bit >>= 1) j ^= bit;
            j ^= bit;
            if (i < j)
            {
                (re[i], re[j]) = (re[j], re[i]);
                (im[i], im[j]) = (im[j], im[i]);
            }
        }

        for (int size = 2; size <= n; size <<= 1)
        {
            double angle = -2 * Math.PI / size;
            double stepRe = Math.Cos(angle), stepIm = Math.Sin(angle);
            for (int start = 0; start < n; start += size)
            {
                double wRe = 1, wIm = 0;
                for (int k = 0; k < size / 2; k++)
                {
                    int a = start + k, b = a + size / 2;
                    double tRe = re[b] * wRe - im[b] * wIm, tIm = re[b] * wIm + im[b] * wRe;
                    re[b] = re[a] - tRe;
                    im[b] = im[a] - tIm;
                    re[a] += tRe;
                    im[a] += tIm;
                    (wRe, wIm) = (wRe * stepRe - wIm * stepIm, wRe * stepIm + wIm * stepRe);
                }
            }
        }
    }
}
