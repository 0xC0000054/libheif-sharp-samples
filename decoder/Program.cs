/*
 * This file is part of heif-dec, an example decoder application for libheif-sharp
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
using Mono.Options;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Metadata.Profiles.Exif;
using SixLabors.ImageSharp.Metadata.Profiles.Icc;
using SixLabors.ImageSharp.PixelFormats;
using System;
using System.Globalization;
using System.IO;

namespace HeifDecoder
{
    class Program
    {
        static void Main(string[] args)
        {
            bool extractDepthImages = false;
            bool extractThumbnailImages = false;
            bool extractPrimaryImage = false;
            bool convertHdrToEightBit = false;
            bool showHelp = false;

            var options = new OptionSet
            {
                "Usage: heif-dec [OPTIONS] input.heif output.png",
                "",
                "Options:",
                { "p|primary", "Extract the primary image (default: extract all top-level images).", (v) => extractPrimaryImage = v != null },
                { "d|depth", "Extract the depth images (if present).", (v) => extractDepthImages = v != null },
                { "t|thumb", "Extract the thumbnail images (if present).", (v) => extractThumbnailImages = v != null },
                { "no-hdr", "Convert HDR images to 8 bits-per-channel.", (v) => convertHdrToEightBit = v != null },
                { "h|help", "Print out this message and exit.", (v) => showHelp = v != null }
            };

            var remaining = options.Parse(args);

            if (showHelp || remaining.Count != 2)
            {
                options.WriteOptionDescriptions(Console.Out);
                return;
            }

            string inputPath = remaining[0];
            string outputPath = remaining[1];

            try
            {
                HeifDecodingOptions decodingOptions = new HeifDecodingOptions
                {
                    ConvertHdrToEightBit = convertHdrToEightBit
                };

                using (var context = new HeifContext())
                {
                    context.ReadFromFile(inputPath);

                    if (extractPrimaryImage)
                    {
                        using (var primaryImage = context.GetPrimaryImageHandle())
                        {
                            ProcessImageHandle(primaryImage, decodingOptions, extractDepthImages, extractThumbnailImages, outputPath);
                        }
                    }
                    else
                    {
                        var topLevelImageIds = context.GetTopLevelImageIds();

                        string imageFileName = AddSuffixToFileName(outputPath, "-{0}");

                        for (int i = 0; i < topLevelImageIds.Count; i++)
                        {
                            using (var imageHandle = context.GetImageHandle(topLevelImageIds[i]))
                            {
                                ProcessImageHandle(imageHandle,
                                                   decodingOptions,
                                                   extractDepthImages,
                                                   extractThumbnailImages,
                                                   string.Format(CultureInfo.CurrentCulture, imageFileName, i));
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
            }
        }

        static string AddSuffixToFileName(string path, string suffix)
        {
            string outputDir = Path.GetDirectoryName(path);
            string fileName = Path.GetFileNameWithoutExtension(path);
            string extension = Path.GetExtension(path);

            return Path.Combine(outputDir, fileName + suffix + extension);
        }

        static unsafe Image CreateEightBitImageWithAlpha(HeifImage heifImage)
        {
            var image = new Image<Rgba32>(heifImage.Width, heifImage.Height);

            var heifPlaneData = heifImage.GetPlane(HeifChannel.Interleaved);

            byte* srcScan0 = (byte*)heifPlaneData.Scan0;
            int srcStride = heifPlaneData.Stride;

            for (int y = 0; y < image.Height; y++)
            {
                byte* src = srcScan0 + (y * srcStride);
                var dst = image.GetPixelRowSpan(y);

                for (int x = 0; x < image.Width; x++)
                {
                    ref var pixel = ref dst[x];

                    pixel.R = src[0];
                    pixel.G = src[1];
                    pixel.B = src[2];
                    pixel.A = src[3];

                    src += 4;
                }
            }

            return image;
        }

        static unsafe Image CreateEightBitImageWithoutAlpha(HeifImage heifImage)
        {
            var image = new Image<Rgb24>(heifImage.Width, heifImage.Height);

            var heifPlaneData = heifImage.GetPlane(HeifChannel.Interleaved);

            byte* srcScan0 = (byte*)heifPlaneData.Scan0;
            int srcStride = heifPlaneData.Stride;

            for (int y = 0; y < image.Height; y++)
            {
                byte* src = srcScan0 + (y * srcStride);
                var dst = image.GetPixelRowSpan(y);

                for (int x = 0; x < image.Width; x++)
                {
                    ref var pixel = ref dst[x];

                    pixel.R = src[0];
                    pixel.G = src[1];
                    pixel.B = src[2];

                    src += 3;
                }
            }

            return image;
        }

        static unsafe Image CreateSixteenBitImageWithAlpha(HeifImage heifImage)
        {
            var image = new Image<Rgba64>(heifImage.Width, heifImage.Height);

            var heifPlaneData = heifImage.GetPlane(HeifChannel.Interleaved);

            byte* srcScan0 = (byte*)heifPlaneData.Scan0;
            int srcStride = heifPlaneData.Stride;

            for (int y = 0; y < image.Height; y++)
            {
                ushort* src = (ushort*)(srcScan0 + (y * srcStride));
                var dst = image.GetPixelRowSpan(y);

                for (int x = 0; x < image.Width; x++)
                {
                    ref var pixel = ref dst[x];

                    pixel.R = src[0];
                    pixel.G = src[1];
                    pixel.B = src[2];
                    pixel.A = src[3];

                    src += 4;
                }
            }

            return image;
        }

        static unsafe Image CreateSixteenBitImageWithoutAlpha(HeifImage heifImage)
        {
            var image = new Image<Rgb48>(heifImage.Width, heifImage.Height);

            var heifPlaneData = heifImage.GetPlane(HeifChannel.Interleaved);

            byte* srcScan0 = (byte*)heifPlaneData.Scan0;
            int srcStride = heifPlaneData.Stride;

            for (int y = 0; y < image.Height; y++)
            {
                ushort* src = (ushort*)(srcScan0 + (y * srcStride));
                var dst = image.GetPixelRowSpan(y);

                for (int x = 0; x < image.Width; x++)
                {
                    ref var pixel = ref dst[x];

                    pixel.R = src[0];
                    pixel.G = src[1];
                    pixel.B = src[2];

                    src += 3;
                }
            }

            return image;
        }

        static void ProcessImageHandle(HeifImageHandle imageHandle,
                                       HeifDecodingOptions decodingOptions,
                                       bool extractDepthImages,
                                       bool extractThumbnailImages,
                                       string outputPath)
        {
            WriteOutputImage(imageHandle, decodingOptions, outputPath);

            if (extractDepthImages)
            {
                if (imageHandle.HasDepthImage)
                {
                    var depthImageIds = imageHandle.GetDepthImageIds();

                    string depthImageFileName;

                    if (depthImageIds.Count == 1)
                    {
                        depthImageFileName = AddSuffixToFileName(outputPath, "-depth");

                        using (var depthImageHandle = imageHandle.GetDepthImage(depthImageIds[0]))
                        {
                            WriteOutputImage(depthImageHandle, decodingOptions, depthImageFileName);
                        }
                    }
                    else
                    {
                        depthImageFileName = AddSuffixToFileName(outputPath, "-depth-{0}");

                        for (int i = 0; i < depthImageIds.Count; i++)
                        {
                            using (var depthImageHandle = imageHandle.GetDepthImage(depthImageIds[i]))
                            {
                                WriteOutputImage(depthImageHandle, decodingOptions, string.Format(CultureInfo.CurrentCulture, depthImageFileName, i));
                            }
                        }
                    }
                }
            }

            if (extractThumbnailImages)
            {
                var thumbnailImageIds = imageHandle.GetThumbnailImageIds();

                if (thumbnailImageIds.Count > 0)
                {
                    string thumbnailFileName;

                    if (thumbnailImageIds.Count == 1)
                    {
                        thumbnailFileName = AddSuffixToFileName(outputPath, "-thumb");
                        using (var thumbnailImageHandle = imageHandle.GetThumbnailImage(thumbnailImageIds[0]))
                        {
                            WriteOutputImage(thumbnailImageHandle, decodingOptions, thumbnailFileName);
                        }
                    }
                    else
                    {
                        thumbnailFileName = AddSuffixToFileName(outputPath, "-thumb-{0}");

                        for (int i = 0; i < thumbnailImageIds.Count; i++)
                        {
                            using (var thumbnailImageHandle = imageHandle.GetThumbnailImage(thumbnailImageIds[i]))
                            {
                                WriteOutputImage(thumbnailImageHandle, decodingOptions, string.Format(CultureInfo.CurrentCulture, thumbnailFileName, i));
                            }
                        }
                    }
                }
            }
        }

        static void WriteOutputImage(HeifImageHandle imageHandle, HeifDecodingOptions decodingOptions, string outputPath)
        {
            Image outputImage;

            HeifChroma chroma;
            bool hasAlpha = imageHandle.HasAlphaChannel;
            int bitDepth = imageHandle.BitDepth;

            if (bitDepth == 8 || decodingOptions.ConvertHdrToEightBit)
            {
                chroma = hasAlpha ? HeifChroma.InterleavedRgba32 : HeifChroma.InterleavedRgb24;
            }
            else
            {
                // Use the native byte order of the operating system.
                if (BitConverter.IsLittleEndian)
                {
                    chroma = hasAlpha ? HeifChroma.InterleavedRgba64LE : HeifChroma.InterleavedRgb48LE;
                }
                else
                {
                    chroma = hasAlpha ? HeifChroma.InterleavedRgba64BE : HeifChroma.InterleavedRgb48BE;
                }
            }

            using (var image = imageHandle.Decode(HeifColorspace.Rgb, chroma, decodingOptions))
            {
                switch (chroma)
                {

                    case HeifChroma.InterleavedRgb24:
                        outputImage = CreateEightBitImageWithoutAlpha(image);
                        break;
                    case HeifChroma.InterleavedRgba32:
                        outputImage = CreateEightBitImageWithAlpha(image);
                        break;
                    case HeifChroma.InterleavedRgb48BE:
                    case HeifChroma.InterleavedRgb48LE:
                        outputImage = CreateSixteenBitImageWithoutAlpha(image);
                        break;
                    case HeifChroma.InterleavedRgba64BE:
                    case HeifChroma.InterleavedRgba64LE:
                        outputImage = CreateSixteenBitImageWithAlpha(image);
                        break;
                    default:
                        throw new InvalidOperationException("Unsupported HeifChroma value.");
                }

                if (image.ColorProfile is HeifIccColorProfile iccProfile)
                {
                    outputImage.Metadata.IccProfile = new IccProfile(iccProfile.GetIccProfileBytes());
                }
            }

            byte[] exif = imageHandle.GetExifMetadata();

            if (exif != null)
            {
                outputImage.Metadata.ExifProfile = new ExifProfile(exif);
                // The HEIF specification states that the EXIF orientation tag is only
                // informational and should not be used to rotate the image.
                // See https://github.com/strukturag/libheif/issues/227#issuecomment-642165942
                outputImage.Metadata.ExifProfile.RemoveValue(ExifTag.Orientation);
            }

            outputImage.SaveAsPng(outputPath);
        }
    }
}
