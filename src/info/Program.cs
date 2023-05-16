/*
 * This file is part of heif-info, an example application for libheif-sharp
 *
 * The MIT License (MIT)
 *
 * Copyright (c) 2020, 2021, 2022, 2023 Nicholas Hayes
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
using System;
using System.Reflection;

namespace HeifInfoSample
{
    internal class Program
    {
        static void Main(string[] args)
        {
            bool showHelp = false;
            bool showVersion = false;

            try
            {
                var options = new OptionSet
                {
                    "Usage: heif-info [OPTIONS] input",
                    "",
                    "Options:",
                    { "h|help", "Print out this message and exit.", (v) => showHelp = v != null },
                    { "v|version", "Print out the application and library version information and exit.", (v) => showVersion = v != null }
                };

                var remaining = options.Parse(args);

                if (showVersion)
                {
                    PrintVersionInfo();
                    return;
                }
                else if (showHelp || remaining.Count != 1)
                {
                    options.WriteOptionDescriptions(Console.Out);
                    return;
                }

                string file = args[0];

                using (HeifContext context = new HeifContext(file))
                {
                    var topLevelImageIds = context.GetTopLevelImageIds();

                    foreach (var imageId in topLevelImageIds)
                    {
                        using (var imageHandle = context.GetImageHandle(imageId))
                        {
                            Console.WriteLine("image: {0}x{1} {2}-bit (id={3}){4}",
                                              imageHandle.Width,
                                              imageHandle.Height,
                                              imageHandle.BitDepth,
                                              imageId,
                                              imageHandle.IsPrimaryImage ? " primary" : string.Empty);
                            WriteThumbnailImageInfo(imageHandle);

                            Console.WriteLine("  color profile: {0}", GetColorProfileDescription(imageHandle));
                            Console.WriteLine("  alpha channel: {0}", GetAlphaChannelDescription(imageHandle));

                            WriteDepthImageInfo(imageHandle);
                            WriteMetadataInfo(imageHandle);

                            if (LibHeifInfo.HaveVersion(1, 16, 0))
                            {
                                WriteTransformationInfo(context, imageHandle);
                                WriteRegionInfo(context, imageHandle);
                                WritePropertyInfo(context, imageHandle);
                            }

                            if (LibHeifInfo.HaveVersion(1, 15, 0))
                            {
                                using (var image = imageHandle.Decode(HeifColorspace.Undefined, HeifChroma.Undefined))
                                {
                                    WritePixelAspectRatio(image.PixelAspectRatio);
                                    WriteContentLightLevelInfo(image.ContentLightLevel);
                                    WriteMasteringDisplayColorVolumeInfo(image.MasteringDisplayColourVolume);
                                }
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

        static void PrintVersionInfo()
        {
            Console.WriteLine("heif-info v{0} LibHeifSharp v{1} libheif v{2}",
                              GetAssemblyFileVersion(typeof(Program)),
                              GetAssemblyFileVersion(typeof(LibHeifInfo)),
                              LibHeifInfo.Version.ToString(3));

            static string GetAssemblyFileVersion(Type type)
            {
                var fileVersionAttribute = type.Assembly.GetCustomAttribute<AssemblyFileVersionAttribute>();

#pragma warning disable IDE0270 // Use coalesce expression
                if (fileVersionAttribute is null)
                {
                    throw new InvalidOperationException($"Failed to get the AssemblyFileVersion for {type.Assembly.FullName}.");
                }
#pragma warning restore IDE0270 // Use coalesce expression

                var trimmedVersion = new Version(fileVersionAttribute.Version);

                return trimmedVersion.ToString(3);
            }
        }

        static string GetAlphaChannelDescription(HeifImageHandle handle)
        {
            string description = "no";

            if (handle.HasAlphaChannel)
            {
                description = handle.IsPremultipliedAlpha ? "yes (premultiplied)" : "yes";
            }

            return description;
        }

        static string GetColorProfileDescription(HeifImageHandle handle)
        {
            string description = "none";

            HeifIccColorProfile icc = handle.IccColorProfile;
            HeifNclxColorProfile nclx = handle.NclxColorProfile;

            if (icc != null)
            {
                description = nclx != null ? "icc, nclx" : "icc";
            }
            else if (nclx != null)
            {
                description = "nclx";
            }

            return description;
        }

        static void WriteContentLightLevelInfo(HeifContentLightLevel contentLightLevel)
        {
            if (contentLightLevel != null)
            {
                Console.WriteLine("  content light level (clli):");
                Console.WriteLine("    max content light level: {0}", contentLightLevel.MaxContentLightLevel);
                Console.WriteLine("    max picture average light level: {0}", contentLightLevel.MaxPictureAverageLightLevel);
            }
        }

        static void WriteDepthImageInfo(HeifImageHandle handle)
        {
            if (handle.HasDepthImage)
            {
                Console.WriteLine("  depth image: yes");

                var depthIds = handle.GetDepthImageIds();

                foreach (var depthId in depthIds)
                {
                    using (HeifImageHandle depthHandle = handle.GetDepthImage(depthId))
                    {
                        Console.WriteLine("    depth: {0}x{1}", handle.Width, handle.Height);
                    }

                    var depthRepresentationInfo = handle.GetDepthRepresentationInfo(depthId);

                    if (depthRepresentationInfo != null)
                    {
                        Console.WriteLine("    z-near: {0}", GetNullableValue(depthRepresentationInfo.ZNear));
                        Console.WriteLine("    z-far: {0}", GetNullableValue(depthRepresentationInfo.ZFar));
                        Console.WriteLine("    d-min: {0}", GetNullableValue(depthRepresentationInfo.DMin));
                        Console.WriteLine("    d-max: {0}", GetNullableValue(depthRepresentationInfo.DMax));
                        Console.WriteLine("    representation: {0}", GetRepresentationString(depthRepresentationInfo.DepthRepresentationType));

                        if (depthRepresentationInfo.DMin.HasValue || depthRepresentationInfo.DMax.HasValue)
                        {
                            Console.WriteLine("    disparity reference view: {0}", depthRepresentationInfo.DisparityReferenceView);
                        }

                        static string GetRepresentationString(HeifDepthRepresentationType type)
                        {
                            switch (type)
                            {
                                case HeifDepthRepresentationType.UniformInverseZ:
                                    return "inverse Z";
                                case HeifDepthRepresentationType.UniformDisparity:
                                    return "uniform disparity";
                                case HeifDepthRepresentationType.UniformZ:
                                    return "uniform Z";
                                case HeifDepthRepresentationType.NonuniformDisparity:
                                    return "non-uniform disparity";
                                default:
                                    return "unknown";
                            }
                        }

                        static string GetNullableValue(double? nullable)
                        {
                            return nullable.HasValue ? nullable.Value.ToString() : "undefined";
                        }
                    }
                }
            }
            else
            {
                Console.WriteLine("  depth image: no");
            }
        }

        static void WriteMasteringDisplayColorVolumeInfo(HeifMasteringDisplayColourVolume data)
        {
            if (data != null)
            {
                var decoded = data.Decode();

                Console.WriteLine("  mastering display color volume:");
                Console.WriteLine("    display primaries (x,y): ({0};{1}), ({2};{3}), ({4};{5})",
                                  decoded.DisplayPrimariesX[0],
                                  decoded.DisplayPrimariesY[0],
                                  decoded.DisplayPrimariesX[1],
                                  decoded.DisplayPrimariesY[1],
                                  decoded.DisplayPrimariesX[2],
                                  decoded.DisplayPrimariesY[2]);
                Console.WriteLine("    white point (x,y): ({0};{1})", decoded.WhitePointX, decoded.WhitePointY);
                Console.WriteLine("    max display mastering luminance: {0}", decoded.MaxDisplayMasteringLuminance);
                Console.WriteLine("    min display mastering luminance: {0}", decoded.MinDisplayMasteringLuminance);
            }
        }

        static void WriteMetadataInfo(HeifImageHandle handle)
        {
            var metadataBlockIds = handle.GetMetadataBlockIds();

            if (metadataBlockIds.Count > 0)
            {
                Console.WriteLine("  metadata:");

                foreach (var metadataBlockId in metadataBlockIds)
                {
                    var metadataInfo = handle.GetMetadataBlockInfo(metadataBlockId);

                    string id = GetMetadataTypeString(metadataInfo);

                    Console.WriteLine("    {0}: {1} bytes", id, metadataInfo.Size);
                }
            }
            else
            {
                Console.WriteLine("  metadata: none");
            }

            static string GetMetadataTypeString(HeifMetadataBlockInfo metadataInfo)
            {
                string itemType = metadataInfo.ItemType;
                string contentType = metadataInfo.ContentType;

                if (itemType == "Exif")
                {
                    return itemType;
                }
                else if (itemType == "mime" && contentType == "application/rdf+xml")
                {
                    return "XMP";
                }
                else
                {
                    return itemType + "/" + contentType;
                }
            }
        }

        static void WritePixelAspectRatio(in HeifPixelAspectRatio pixelAspectRatio)
        {
            if (!pixelAspectRatio.HasSquareAspectRatio)
            {
                Console.WriteLine("  pixel aspect ratio: {0}", pixelAspectRatio.ToString());
            }
        }

        static void WritePropertyInfo(HeifContext context, HeifImageHandle imageHandle)
        {
            var userDescriptions = context.GetUserDescriptionProperties(imageHandle);

            Console.WriteLine("  properties:");

            foreach (var item in userDescriptions)
            {
                Console.WriteLine("    user description:");
                Console.WriteLine("      language: {0}", item.Language);
                Console.WriteLine("      name: {0}", item.Name);
                Console.WriteLine("      description: {0}", item.Description);
                Console.WriteLine("      tags: {0}", item.Tags);
            }
        }

        static void WriteRegionInfo(HeifContext context, HeifImageHandle imageHandle)
        {
            Console.WriteLine("  region annotations:");

            var ids = imageHandle.GetRegionItemIds();

            foreach (var id in ids)
            {
                using (HeifRegionItem regionItem = context.GetRegionItem(id))
                {
                    var regions = regionItem.GetRegionGeometries();

                    Console.WriteLine("    id={0} reference_width={1} reference_height={2} {3} regions",
                                      regionItem.Id,
                                      regionItem.ReferenceWidth,
                                      regionItem.ReferenceHeight,
                                      regions.Count);

                    foreach (var region in regions)
                    {
                        Console.WriteLine("      {0}", region.ToString());
                    }

                    var userDescriptions = context.GetUserDescriptionProperties(regionItem.Id);

                    foreach (var item in userDescriptions)
                    {
                        Console.WriteLine("    user description:");
                        Console.WriteLine("      language: {0}", item.Language);
                        Console.WriteLine("      name: {0}", item.Name);
                        Console.WriteLine("      description: {0}", item.Description);
                        Console.WriteLine("      tags: {0}", item.Tags);
                    }
                }
            }
        }

        static void WriteThumbnailImageInfo(HeifImageHandle handle)
        {
            var thumbnailIds = handle.GetThumbnailImageIds();

            if (thumbnailIds.Count > 0)
            {
                Console.WriteLine("  thumbnails:");

                foreach (var thumbnailId in thumbnailIds)
                {
                    using (HeifImageHandle thumbnail = handle.GetThumbnailImage(thumbnailId))
                    {
                        Console.WriteLine("    thumbnail: {0}x{1} {2}-bit",
                                          thumbnail.Width,
                                          thumbnail.Height,
                                          thumbnail.BitDepth);
                    }
                }
            }
            else
            {
                Console.WriteLine("  thumbnails: none");
            }
        }

        static void WriteTransformationInfo(HeifContext context, HeifImageHandle imageHandle)
        {
            var transformations = context.GetTransformationProperties(imageHandle);

            if (transformations.Count > 0)
            {
                Console.WriteLine("  transformations:");

                foreach (var item in transformations)
                {
                    Console.WriteLine("    {0}", item.ToString());
                }
            }
            else
            {
                Console.WriteLine("  transformations: none");
            }
        }
    }
}