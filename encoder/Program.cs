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
using Mono.Options;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Bmp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Formats.Tga;
using SixLabors.ImageSharp.Metadata;
using SixLabors.ImageSharp.PixelFormats;
using System;
using System.Globalization;

namespace HeifEncoderSample
{
    class Program
    {
        static void Main(string[] args)
        {
            int quality = 50;
            bool avif = false;
            bool lossless = false;
            bool saveAlphaChannel = true;
            bool saveThumbnailAlphaChannel = true;
            int thumbnailBoundingBoxSize = 0;
            bool showHelp = false;

            try
            {
                var options = new OptionSet
                {
                    "Usage: heif-enc [OPTIONS] input output",
                    "",
                    "Options:",
                    { "q|quality=", "The lossy encode quality (default: 50).", (v) => quality = int.Parse(v, NumberStyles.Integer, CultureInfo.InvariantCulture) },
                    { "L|lossless", "Use lossless compression (default: false)", (v) => lossless = v != null },
                    { "t|thumbnail-bounding-box-size=", "The size of the thumbnail bounding box in pixels.", (v) => thumbnailBoundingBoxSize = int.Parse(v, NumberStyles.Integer, CultureInfo.InvariantCulture) },
                    { "no-alpha", "Do not save the image alpha channel. (default: false)", (v) => saveAlphaChannel = v is null },
                    { "no-thumbnail-alpha", "Do not save the thumbnail image alpha channel. (default: false)", (v) => saveThumbnailAlphaChannel = v is null },
                    { "h|help", "Print out this message and exit.", (v) => showHelp = v != null }
                };

                if (LibHeifInfo.HaveEncoder(HeifCompressionFormat.Av1))
                {
                    options.Add("A|avif", "Encode as AVIF (default: HEVC)", (v) => avif = v != null);
                }

                var remaining = options.Parse(args);

                if (showHelp || remaining.Count != 2)
                {
                    options.WriteOptionDescriptions(Console.Out);
                    return;
                }

                if (quality < 0 || quality > 100)
                {
                    Console.WriteLine("The quality parameter must be between 0 and 100.");
                    return;
                }

                var format = avif ? HeifCompressionFormat.Av1 : HeifCompressionFormat.Hevc;

                string inputPath = remaining[0];
                string outputPath = remaining[1];

                HeifImage heifImage = null;

                try
                {
                    ImageMetadata metadata;

                    if (ImageMayHaveTransparency(inputPath))
                    {
                        var image = Image.Load<Rgba32>(inputPath);
                        metadata = image.Metadata;

                        heifImage = ImageConversion.ConvertToHeifImage(image);
                    }
                    else
                    {
                        var image = Image.Load<Rgb24>(inputPath);
                        metadata = image.Metadata;

                        heifImage = ImageConversion.ConvertToHeifImage(image);
                    }

                    if (lossless)
                    {
                        // The Identity matrix coefficient places the RGB values into the YUV planes without any conversion.
                        // This reduces the compression efficiency, but allows for fully lossless encoding.
                        heifImage.ColorProfile = new HeifNclxColorProfile(ColorPrimaries.BT709,
                                                                          TransferCharacteristics.Srgb,
                                                                          MatrixCoefficients.Identity,
                                                                          fullRange: true);
                    }
                    else if (metadata.IccProfile != null)
                    {
                        heifImage.ColorProfile = new HeifIccColorProfile(metadata.IccProfile.ToByteArray());
                    }
                    else
                    {
                        heifImage.ColorProfile = new HeifNclxColorProfile(ColorPrimaries.BT709,
                                                                          TransferCharacteristics.Srgb,
                                                                          MatrixCoefficients.BT601,
                                                                          fullRange: true);
                    }

                    HeifEncodingOptions encodingOptions = new HeifEncodingOptions { SaveAlphaChannel = saveAlphaChannel };

                    using (var context = new HeifContext())
                    {
                        using (var encoder = context.GetEncoder(format))
                        {
                            if (lossless)
                            {
                                encoder.SetLossless(true);
                                // Lossless encoding requires YUV 4:4:4 chroma.
                                encoder.SetParameter("chroma", "444");
                            }
                            else
                            {
                                encoder.SetLossyQuality(quality);
                            }

                            if (metadata.ExifProfile is null && thumbnailBoundingBoxSize == 0)
                            {
                                context.EncodeImage(heifImage, encoder, encodingOptions);
                            }
                            else
                            {
                                using (var imageHandle = context.EncodeImageAndReturnHandle(heifImage, encoder, encodingOptions))
                                {
                                    if (metadata.ExifProfile != null)
                                    {
                                        context.AddExifMetadata(imageHandle, metadata.ExifProfile.ToByteArray());
                                    }

                                    if (thumbnailBoundingBoxSize > 0)
                                    {
                                        HeifEncodingOptions thumbnailEncodingOptions = new HeifEncodingOptions
                                        {
                                            SaveAlphaChannel = saveAlphaChannel && saveThumbnailAlphaChannel
                                        };

                                        context.EncodeThumbnail(thumbnailBoundingBoxSize, heifImage, imageHandle, encoder, thumbnailEncodingOptions);
                                    }
                                }
                            }
                            context.WriteToFile(outputPath);
                        }
                    }
                }
                finally
                {
                    heifImage?.Dispose();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
            }
        }

        static bool ImageMayHaveTransparency(string path)
        {
            bool mayHaveTransparency = true;

            var imageInfo = Image.Identify(path, out var imageFormat);

            if (imageFormat is PngFormat)
            {
                var pngMeta = imageInfo.Metadata.GetPngMetadata();

                mayHaveTransparency = pngMeta.HasTransparency;
            }
            else if (imageFormat is JpegFormat)
            {
                mayHaveTransparency = false;
            }
            else if (imageFormat is BmpFormat)
            {
                var bmpMeta = imageInfo.Metadata.GetBmpMetadata();

                mayHaveTransparency = bmpMeta.BitsPerPixel == BmpBitsPerPixel.Pixel32;
            }
            else if (imageFormat is TgaFormat)
            {
                var tgaMeta = imageInfo.Metadata.GetTgaMetadata();

                mayHaveTransparency = tgaMeta.BitsPerPixel == TgaBitsPerPixel.Pixel32
                                      && tgaMeta.AlphaChannelBits != 0;
            }

            return mayHaveTransparency;
        }

    }
}
