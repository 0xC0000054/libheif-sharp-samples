/*
 * This file is part of heif-enc, an example encoder application for libheif-sharp
 *
 * The MIT License (MIT)
 *
 * Copyright (c) 2020, 2021 Nicholas Hayes
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
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace HeifEncoderSample
{
    class Program
    {
        static void Main(string[] args)
        {
            int quality = 50;
            bool avif = false;
            bool lossless = false;
            bool listEncoders = false;
            string encoderId = null;
            bool listEncoderParameters = false;
            var encoderParameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
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
                    { "A|avif", "Encode as AVIF (default: HEVC)", (v) => avif = v != null },
                    { "q|quality=", "The lossy encode quality (default: 50).", (v) => quality = int.Parse(v, NumberStyles.Integer, CultureInfo.InvariantCulture) },
                    { "L|lossless", "Use lossless compression (default: false)", (v) => lossless = v != null },
                    { "t|thumbnail-bounding-box-size=", "The size of the thumbnail bounding box in pixels.", (v) => thumbnailBoundingBoxSize = int.Parse(v, NumberStyles.Integer, CultureInfo.InvariantCulture) },
                    { "e|encoder=", "Use the specified encoder.", (v) => encoderId = v },
                    { "E|list-encoders", "Show a list of the available encoders.", (v) => listEncoders = v != null },
                    { "p|encoder-parameter=", "Set the specified parameter in the encoder settings, uses a key=value format.", (k, v) => encoderParameters.Add(k, v) },
                    { "P|list-encoder-parameters", "Show a list of the available encoder parameters.", (v) => listEncoderParameters = v != null },
                    { "no-alpha", "Do not save the image alpha channel. (default: false)", (v) => saveAlphaChannel = v is null },
                    { "no-thumbnail-alpha", "Do not save the thumbnail image alpha channel. (default: false)", (v) => saveThumbnailAlphaChannel = v is null },
                    { "h|help", "Print out this message and exit.", (v) => showHelp = v != null }
                };

                var remaining = options.Parse(args);

                var format = avif ? HeifCompressionFormat.Av1 : HeifCompressionFormat.Hevc;

                if (!LibHeifInfo.HaveEncoder(format))
                {
                    string formatName = avif ? "AV1" : "HEVC";
                    Console.WriteLine($"No { formatName } encoder available.");
                    return;
                }

                if (listEncoders)
                {
                    using (var context = new HeifContext())
                    {
                        var encoderDescriptors = context.GetEncoderDescriptors(format);

                        PrintEncoderList(encoderDescriptors);
                    }
                    return;
                }
                else if (showHelp)
                {
                    options.WriteOptionDescriptions(Console.Out);
                    return;
                }

                if (quality < 0 || quality > 100)
                {
                    Console.WriteLine("The quality parameter must be between 0 and 100.");
                    return;
                }

                using (var context = new HeifContext())
                {
                    HeifEncoder encoder = null;

                    if (encoderId is null)
                    {
                        encoder = context.GetEncoder(format);
                    }
                    else
                    {
                        var encoderDescriptors = context.GetEncoderDescriptors(format);

                        for (int i = 0; i < encoderDescriptors.Count; i++)
                        {
                            var descriptor = encoderDescriptors[i];

                            if (encoderId.Equals(descriptor.IdName, StringComparison.Ordinal))
                            {
                                encoder = context.GetEncoder(descriptor);
                                break;
                            }
                        }

                        if (encoder is null)
                        {
                            Console.WriteLine("Invalid encoder ID, please choose one from the list below:");
                            PrintEncoderList(encoderDescriptors);
                            return;
                        }
                    }

                    try
                    {
                        if (listEncoderParameters)
                        {
                            PrintEncoderParameterList(encoder);
                        }
                        else
                        {
                            if (remaining.Count != 2)
                            {
                                options.WriteOptionDescriptions(Console.Out);
                                return;
                            }

                            string inputPath = remaining[0];
                            string outputPath = remaining[1];

                            using (var heifImage = CreateHeifImage(inputPath, lossless, out var metadata))
                            {
                                if (encoderParameters.Count > 0)
                                {
                                    foreach (var item in encoderParameters)
                                    {
                                        encoder.SetParameter(item.Key, item.Value);
                                    }
                                }

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

                                var encodingOptions = new HeifEncodingOptions { SaveAlphaChannel = saveAlphaChannel };

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
                                            var thumbnailEncodingOptions = new HeifEncodingOptions
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
                        encoder?.Dispose();
                    }
                }

            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
            }
        }

        static HeifImage CreateHeifImage(string inputPath, bool lossless, out ImageMetadata metadata)
        {
            HeifImage heifImage = null;
            HeifImage temp = null;

            try
            {
                if (ImageMayHaveTransparency(inputPath))
                {
                    var image = Image.Load<Rgba32>(inputPath);
                    metadata = image.Metadata;

                    temp = ImageConversion.ConvertToHeifImage(image);
                }
                else
                {
                    var image = Image.Load<Rgb24>(inputPath);
                    metadata = image.Metadata;

                    temp = ImageConversion.ConvertToHeifImage(image);
                }

                if (lossless)
                {
                    // The Identity matrix coefficient places the RGB values into the YUV planes without any conversion.
                    // This reduces the compression efficiency, but allows for fully lossless encoding.
                    temp.ColorProfile = new HeifNclxColorProfile(ColorPrimaries.BT709,
                                                                 TransferCharacteristics.Srgb,
                                                                 MatrixCoefficients.Identity,
                                                                 fullRange: true);
                }
                else if (metadata.IccProfile != null)
                {
                    temp.ColorProfile = new HeifIccColorProfile(metadata.IccProfile.ToByteArray());
                }
                else
                {
                    temp.ColorProfile = new HeifNclxColorProfile(ColorPrimaries.BT709,
                                                                 TransferCharacteristics.Srgb,
                                                                 MatrixCoefficients.BT601,
                                                                 fullRange: true);
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

        static void PrintEncoderList(IReadOnlyList<HeifEncoderDescriptor> encoderDescriptors)
        {
            for (int i = 0; i < encoderDescriptors.Count; i++)
            {
                var encoderDescriptor = encoderDescriptors[i];

                Console.WriteLine("{0} = {1}", encoderDescriptor.IdName, encoderDescriptor.Name);
            }
        }

        static void PrintEncoderParameterList(HeifEncoder encoder)
        {
            var encoderParameters = encoder.EncoderParameters;

            for (int i = 0; i < encoderParameters.Count; i++)
            {
                var parameter = encoderParameters[i];

                Console.Write(parameter.Name);

                switch (parameter.ParameterType)
                {
                    case HeifEncoderParameterType.Boolean:
                        var booleanEncoderParameter = (HeifBooleanEncoderParameter)parameter;

                        if (booleanEncoderParameter.HasDefault)
                        {
                            Console.Write($", default={ booleanEncoderParameter.DefaultValue }");
                        }
                        break;
                    case HeifEncoderParameterType.Integer:
                        var integerEncoderParameter = (HeifIntegerEncoderParameter)parameter;

                        if (integerEncoderParameter.HasDefault)
                        {
                            Console.Write($", default={ integerEncoderParameter.DefaultValue }");
                        }

                        if (integerEncoderParameter.HasMinimumMaximum)
                        {
                            Console.Write($", [{ integerEncoderParameter.Minimum },{ integerEncoderParameter.Maximum }]");
                        }
                        break;
                    case HeifEncoderParameterType.String:
                        var stringEncoderParameter = (HeifStringEncoderParameter)parameter;

                        if (stringEncoderParameter.HasDefault)
                        {
                            Console.Write($", default={ stringEncoderParameter.DefaultValue }");
                        }

                        var validValues = stringEncoderParameter.ValidValues;

                        if (validValues.Count > 0)
                        {
                            Console.Write(", {");

                            for (int j = 0; j < validValues.Count; j++)
                            {
                                if (j > 0)
                                {
                                    Console.Write(',');
                                }

                                Console.Write(validValues[j]);
                            }

                            Console.Write('}');
                        }
                        break;
                    default:
                        throw new InvalidOperationException($"Unknown { nameof(HeifEncoderParameterType) }, { parameter.ParameterType }.");
                }

                Console.Write(Environment.NewLine);
            }
        }
    }
}
