/*
 * This file is part of heif-dec, an example decoder application for libheif-sharp
 *
 * The MIT License (MIT)
 *
 * Copyright (c) 2020, 2021, 2022 Nicholas Hayes
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
using SixLabors.ImageSharp.Metadata.Profiles.Xmp;
using SixLabors.ImageSharp.PixelFormats;
using System;
using System.Globalization;
using System.IO;

namespace HeifDecoderSample
{
    class Program
    {
        private static readonly Lazy<char[]> InvalidFileNameChars = new(Path.GetInvalidFileNameChars);

        static void Main(string[] args)
        {
            bool extractDepthImages = false;
            bool extractThumbnailImages = false;
            bool extractVendorAuxiliaryImages = false;
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
                { "x|vendor-auxiliary", "Extract the vendor-specific auxiliary images (if present).", (v) => extractVendorAuxiliaryImages = v != null },
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
                var decodingOptions = new HeifDecodingOptions
                {
                    ConvertHdrToEightBit = convertHdrToEightBit
                };

                using (var context = new HeifContext(inputPath))
                {
                    if (extractPrimaryImage)
                    {
                        using (var primaryImage = context.GetPrimaryImageHandle())
                        {
                            ProcessImageHandle(primaryImage,
                                               decodingOptions,
                                               extractDepthImages,
                                               extractThumbnailImages,
                                               extractVendorAuxiliaryImages,
                                               outputPath);
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
                                                   extractVendorAuxiliaryImages,
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

        static string SanitizeFileName(string fileName)
        {
            char[] invalidChars = InvalidFileNameChars.Value;

            foreach (char invalid in invalidChars)
            {
                fileName = fileName.Replace(invalid, '_');
            }

            return fileName;
        }

        static unsafe Image CreateEightBitImageWithAlpha(HeifImage heifImage, bool premultiplied)
        {
            var image = new Image<Rgba32>(heifImage.Width, heifImage.Height);

            var heifPlaneData = heifImage.GetPlane(HeifChannel.Interleaved);

            byte* srcScan0 = (byte*)heifPlaneData.Scan0;
            int srcStride = heifPlaneData.Stride;

            image.ProcessPixelRows(accessor =>
            {
                for (int y = 0; y < accessor.Height; y++)
                {
                    byte* src = srcScan0 + (y * srcStride);
                    var dst = accessor.GetRowSpan(y);

                    for (int x = 0; x < accessor.Width; x++)
                    {
                        ref var pixel = ref dst[x];

                        if (premultiplied)
                        {
                            byte alpha = src[3];

                            switch (alpha)
                            {
                                case 0:
                                    pixel.R = 0;
                                    pixel.G = 0;
                                    pixel.B = 0;
                                    break;
                                case 255:
                                    pixel.R = src[0];
                                    pixel.G = src[1];
                                    pixel.B = src[2];
                                    break;
                                default:
                                    pixel.R = (byte)Math.Min(MathF.Round(src[0] * 255f / alpha), 255);
                                    pixel.G = (byte)Math.Min(MathF.Round(src[1] * 255f / alpha), 255);
                                    pixel.B = (byte)Math.Min(MathF.Round(src[2] * 255f / alpha), 255);
                                    break;
                            }
                        }
                        else
                        {
                            pixel.R = src[0];
                            pixel.G = src[1];
                            pixel.B = src[2];
                        }
                        pixel.A = src[3];

                        src += 4;
                    }
                }
            });

            return image;
        }

        static unsafe Image CreateEightBitImageWithoutAlpha(HeifImage heifImage)
        {
            var image = new Image<Rgb24>(heifImage.Width, heifImage.Height);

            var heifPlaneData = heifImage.GetPlane(HeifChannel.Interleaved);

            byte* srcScan0 = (byte*)heifPlaneData.Scan0;
            int srcStride = heifPlaneData.Stride;

            image.ProcessPixelRows(accessor =>
            {
                for (int y = 0; y < accessor.Height; y++)
                {
                    byte* src = srcScan0 + (y * srcStride);
                    var dst = accessor.GetRowSpan(y);

                    for (int x = 0; x < accessor.Width; x++)
                    {
                        ref var pixel = ref dst[x];

                        pixel.R = src[0];
                        pixel.G = src[1];
                        pixel.B = src[2];

                        src += 3;
                    }
                }
            });

            return image;
        }

        static unsafe Image CreateSixteenBitImageWithAlpha(HeifImage heifImage, bool premultiplied, int bitDepth)
        {
            var image = new Image<Rgba64>(heifImage.Width, heifImage.Height);

            var heifPlaneData = heifImage.GetPlane(HeifChannel.Interleaved);

            byte* srcScan0 = (byte*)heifPlaneData.Scan0;
            int srcStride = heifPlaneData.Stride;

            int maxChannelValue = (1 << bitDepth) - 1;
            float maxChannelValueFloat = maxChannelValue;

            image.ProcessPixelRows(accessor =>
            {
                for (int y = 0; y < accessor.Height; y++)
                {
                    ushort* src = (ushort*)(srcScan0 + (y * srcStride));
                    var dst = accessor.GetRowSpan(y);

                    for (int x = 0; x < accessor.Width; x++)
                    {
                        ref var pixel = ref dst[x];

                        if (premultiplied)
                        {
                            ushort alpha = src[3];

                            if (alpha == maxChannelValue)
                            {
                                pixel.R = src[0];
                                pixel.G = src[1];
                                pixel.B = src[2];
                            }
                            else
                            {
                                switch (alpha)
                                {
                                    case 0:
                                        pixel.R = 0;
                                        pixel.G = 0;
                                        pixel.B = 0;
                                        break;
                                    default:
                                        pixel.R = (ushort)Math.Min(MathF.Round(src[0] * maxChannelValueFloat / alpha), maxChannelValue);
                                        pixel.G = (ushort)Math.Min(MathF.Round(src[1] * maxChannelValueFloat / alpha), maxChannelValue);
                                        pixel.B = (ushort)Math.Min(MathF.Round(src[2] * maxChannelValueFloat / alpha), maxChannelValue);
                                        break;
                                }
                            }
                        }
                        else
                        {
                            pixel.R = src[0];
                            pixel.G = src[1];
                            pixel.B = src[2];
                        }
                        pixel.A = src[3];

                        src += 4;
                    }
                }
            });

            return image;
        }

        static unsafe Image CreateSixteenBitImageWithoutAlpha(HeifImage heifImage)
        {
            var image = new Image<Rgb48>(heifImage.Width, heifImage.Height);

            var heifPlaneData = heifImage.GetPlane(HeifChannel.Interleaved);

            byte* srcScan0 = (byte*)heifPlaneData.Scan0;
            int srcStride = heifPlaneData.Stride;

            image.ProcessPixelRows(accessor =>
            {
                for (int y = 0; y < accessor.Height; y++)
                {
                    ushort* src = (ushort*)(srcScan0 + (y * srcStride));
                    var dst = accessor.GetRowSpan(y);

                    for (int x = 0; x < accessor.Width; x++)
                    {
                        ref var pixel = ref dst[x];

                        pixel.R = src[0];
                        pixel.G = src[1];
                        pixel.B = src[2];

                        src += 3;
                    }
                }
            });

            return image;
        }

        static void ProcessImageHandle(HeifImageHandle imageHandle,
                                       HeifDecodingOptions decodingOptions,
                                       bool extractDepthImages,
                                       bool extractThumbnailImages,
                                       bool extractVendorAuxiliaryImages,
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

            if (extractVendorAuxiliaryImages)
            {
                var vendorAuxImageIds = imageHandle.GetAuxiliaryImageIds();

                if (vendorAuxImageIds.Count > 0)
                {
                    string vendorAuxFileName;

                    if (vendorAuxImageIds.Count == 1)
                    {
                        using (var vendorAuxImageHandle = imageHandle.GetAuxiliaryImage(vendorAuxImageIds[0]))
                        {
                            string type = vendorAuxImageHandle.GetAuxiliaryType();

                            vendorAuxFileName = AddSuffixToFileName(outputPath, "-" + SanitizeFileName(type));
                            WriteOutputImage(vendorAuxImageHandle, decodingOptions, vendorAuxFileName);
                        }
                    }
                    else
                    {
                        for (int i = 0; i < vendorAuxImageIds.Count; i++)
                        {
                            using (var vendorAuxImageHandle = imageHandle.GetThumbnailImage(vendorAuxImageIds[i]))
                            {
                                string type = vendorAuxImageHandle.GetAuxiliaryType();

                                vendorAuxFileName = AddSuffixToFileName(outputPath, string.Format(CultureInfo.CurrentCulture,
                                                                                                  "-{0}-{1}",
                                                                                                  SanitizeFileName(type),
                                                                                                  i));

                                WriteOutputImage(vendorAuxImageHandle, decodingOptions, vendorAuxFileName);
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
                        outputImage = CreateEightBitImageWithAlpha(image, imageHandle.IsPremultipliedAlpha);
                        break;
                    case HeifChroma.InterleavedRgb48BE:
                    case HeifChroma.InterleavedRgb48LE:
                        outputImage = CreateSixteenBitImageWithoutAlpha(image);
                        break;
                    case HeifChroma.InterleavedRgba64BE:
                    case HeifChroma.InterleavedRgba64LE:
                        outputImage = CreateSixteenBitImageWithAlpha(image, imageHandle.IsPremultipliedAlpha, imageHandle.BitDepth);
                        break;
                    default:
                        throw new InvalidOperationException("Unsupported HeifChroma value.");
                }

                if (image.IccColorProfile != null)
                {
                    outputImage.Metadata.IccProfile = new IccProfile(image.IccColorProfile.GetIccProfileBytes());
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

            byte[] xmp = imageHandle.GetXmpMetadata();

            if (xmp != null)
            {
                outputImage.Metadata.XmpProfile = new XmpProfile(xmp);
            }

            outputImage.SaveAsPng(outputPath);
        }
    }
}
