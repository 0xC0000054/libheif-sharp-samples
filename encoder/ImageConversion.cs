/*
 * This file is part of heif-enc, an example encoder application for libheif-sharp
 *
 * The MIT License (MIT)
 *
 * Copyright (c) 2020 Nicholas Hayes
 *
 * Permission is hereby granted, free of charge, to any person obtaining a copy
 * of this software and associated documentation files (the "Software"), to deal
 * in the Software without restriction, including without limitation the rights
 * to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
 * copies of the Software, and to permit persons to whom the Software is
 * furnished to do so, subject to the following conditions:
 *
 * The above copyright notice and this permission notice shall be included in all
 * copies or substantial portions of the Software.
 *
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
 * IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
 * FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
 * AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
 * LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
 * OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
 * SOFTWARE.
 *
 */

using LibHeifSharp;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace HeifEncoderSample
{
    internal static class ImageConversion
    {
        public static HeifImage ConvertToHeifImage(Image<Rgb24> image)
        {
            bool isGrayscale = IsGrayscale(image);

            var colorspace = isGrayscale ? HeifColorspace.Monochrome : HeifColorspace.Rgb;
            var chroma = colorspace == HeifColorspace.Monochrome ? HeifChroma.Monochrome : HeifChroma.InterleavedRgb24;

            HeifImage heifImage = null;
            HeifImage temp = null;

            try
            {
                temp = new HeifImage(image.Width, image.Height, colorspace, chroma);

                if (colorspace == HeifColorspace.Monochrome)
                {
                    temp.AddPlane(HeifChannel.Y, image.Width, image.Height, 8);

                    CopyGrayscale(image, temp);
                }
                else
                {
                    temp.AddPlane(HeifChannel.Interleaved, image.Width, image.Height, 8);

                    CopyRgb(image, temp);
                }

                heifImage = temp;
                temp = null;
            }
            finally
            {
                temp?.Dispose();
            }

            return heifImage;
        }

        public static HeifImage ConvertToHeifImage(Image<Rgba32> image)
        {
            AnalyzeImage(image, out bool isGrayscale, out bool hasTransparency);

            var colorspace = isGrayscale ? HeifColorspace.Monochrome : HeifColorspace.Rgb;
            HeifChroma chroma;

            if (colorspace == HeifColorspace.Monochrome)
            {
                chroma = HeifChroma.Monochrome;
            }
            else
            {
                chroma = hasTransparency ? HeifChroma.InterleavedRgba32 : HeifChroma.InterleavedRgb24;
            }

            HeifImage heifImage = null;
            HeifImage temp = null;

            try
            {
                temp = new HeifImage(image.Width, image.Height, colorspace, chroma);

                if (colorspace == HeifColorspace.Monochrome)
                {
                    temp.AddPlane(HeifChannel.Y, image.Width, image.Height, 8);

                    if (hasTransparency)
                    {
                        temp.AddPlane(HeifChannel.Alpha, image.Width, image.Height, 8);
                    }

                    CopyGrayscale(image, temp, hasTransparency);
                }
                else
                {
                    temp.AddPlane(HeifChannel.Interleaved, image.Width, image.Height, 8);

                    CopyRgb(image, temp, hasTransparency);
                }

                heifImage = temp;
                temp = null;
            }
            finally
            {
                temp?.Dispose();
            }

            return heifImage;
        }

        private static void AnalyzeImage(Image<Rgba32> image, out bool isGrayscale, out bool hasTransparency)
        {
            isGrayscale = true;
            hasTransparency = false;

            for (int y = 0; y < image.Height; y++)
            {
                var src = image.GetPixelRowSpan(y);

                for (int x = 0; x < image.Width; x++)
                {
                    ref var pixel = ref src[x];

                    if (!(pixel.R == pixel.G && pixel.G == pixel.B))
                    {
                        isGrayscale = false;
                    }

                    if (pixel.A < 255)
                    {
                        hasTransparency = true;
                    }
                }
            }
        }

        private static unsafe void CopyGrayscale(Image<Rgba32> image, HeifImage heifImage, bool hasTransparency)
        {
            var grayPlane = heifImage.GetPlane(HeifChannel.Y);

            byte* grayPlaneScan0 = (byte*)grayPlane.Scan0;
            int grayPlaneStride = grayPlane.Stride;

            if (hasTransparency)
            {
                var alphaPlane = heifImage.GetPlane(HeifChannel.Alpha);

                byte* alphaPlaneScan0 = (byte*)alphaPlane.Scan0;
                int alphaPlaneStride = alphaPlane.Stride;

                for (int y = 0; y < image.Height; y++)
                {
                    var src = image.GetPixelRowSpan(y);
                    byte* dst = grayPlaneScan0 + (y * grayPlaneStride);
                    byte* dstAlpha = alphaPlaneScan0 + (y * alphaPlaneStride);

                    for (int x = 0; x < image.Width; x++)
                    {
                        ref var pixel = ref src[x];

                        dst[0] = pixel.R;
                        dstAlpha[0] = pixel.A;

                        dst++;
                        dstAlpha++;
                    }
                }
            }
            else
            {
                for (int y = 0; y < image.Height; y++)
                {
                    var src = image.GetPixelRowSpan(y);
                    byte* dst = grayPlaneScan0 + (y * grayPlaneStride);

                    for (int x = 0; x < image.Width; x++)
                    {
                        ref var pixel = ref src[x];

                        dst[0] = pixel.R;

                        dst++;
                    }
                }
            }
        }

        private static unsafe void CopyGrayscale(Image<Rgb24> image, HeifImage heifImage)
        {
            var grayPlane = heifImage.GetPlane(HeifChannel.Y);

            byte* grayPlaneScan0 = (byte*)grayPlane.Scan0;
            int grayPlaneStride = grayPlane.Stride;


            for (int y = 0; y < image.Height; y++)
            {
                var src = image.GetPixelRowSpan(y);
                byte* dst = grayPlaneScan0 + (y * grayPlaneStride);

                for (int x = 0; x < image.Width; x++)
                {
                    ref var pixel = ref src[x];

                    dst[0] = pixel.R;

                    dst++;
                }
            }
        }

        private static unsafe void CopyRgb(Image<Rgba32> image, HeifImage heifImage, bool hasTransparency)
        {
            var interleavedData = heifImage.GetPlane(HeifChannel.Interleaved);

            byte* srcScan0 = (byte*)interleavedData.Scan0;
            int srcStride = interleavedData.Stride;

            if (hasTransparency)
            {
                for (int y = 0; y < image.Height; y++)
                {
                    var src = image.GetPixelRowSpan(y);
                    byte* dst = srcScan0 + (y * srcStride);

                    for (int x = 0; x < image.Width; x++)
                    {
                        ref var pixel = ref src[x];

                        dst[0] = pixel.R;
                        dst[1] = pixel.G;
                        dst[2] = pixel.B;
                        dst[3] = pixel.A;

                        dst += 4;
                    }
                }
            }
            else
            {
                for (int y = 0; y < image.Height; y++)
                {
                    var src = image.GetPixelRowSpan(y);
                    byte* dst = srcScan0 + (y * srcStride);

                    for (int x = 0; x < image.Width; x++)
                    {
                        ref var pixel = ref src[x];

                        dst[0] = pixel.R;
                        dst[1] = pixel.G;
                        dst[2] = pixel.B;

                        dst += 3;
                    }
                }
            }
        }

        private static unsafe void CopyRgb(Image<Rgb24> image, HeifImage heifImage)
        {
            var interleavedData = heifImage.GetPlane(HeifChannel.Interleaved);

            byte* srcScan0 = (byte*)interleavedData.Scan0;
            int srcStride = interleavedData.Stride;


            for (int y = 0; y < image.Height; y++)
            {
                var src = image.GetPixelRowSpan(y);
                byte* dst = srcScan0 + (y * srcStride);

                for (int x = 0; x < image.Width; x++)
                {
                    ref var pixel = ref src[x];

                    dst[0] = pixel.R;
                    dst[1] = pixel.G;
                    dst[2] = pixel.B;

                    dst += 3;
                }
            }
        }

        private static bool IsGrayscale(Image<Rgb24> image)
        {
            for (int y = 0; y < image.Height; y++)
            {
                var src = image.GetPixelRowSpan(y);

                for (int x = 0; x < image.Width; x++)
                {
                    ref var pixel = ref src[x];

                    if (!(pixel.R == pixel.G && pixel.G == pixel.B))
                    {
                        return false;
                    }
                }
            }

            return true;
        }
    }
}
