/*
 * This file is part of libheif-sharp-samples, a collection of example applications
 * for libheif-sharp
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

using System.Reflection;
using System;
using System.Runtime.InteropServices;

namespace LibHeifSharpSamples
{
    internal static class LibHeifSharpDllImportResolver
    {
        private static IntPtr cachedLibHeifModule = IntPtr.Zero;
        private static bool firstRequestForLibHeif = true;

        /// <summary>
        /// Registers the <see cref="DllImportResolver"/> for the LibHeifSharp assembly.
        /// </summary>
        public static void Register()
        {
            // The runtime will execute the specified callback when it needs to resolve a native library
            // import for the LibHeifSharp assembly.
            NativeLibrary.SetDllImportResolver(typeof(LibHeifSharp.LibHeifInfo).Assembly, Resolver);
        }

        private static IntPtr Resolver(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
        {
            // We only care about a native library named libheif, the runtime will use
            // its default behavior for any other native library.
            if (string.Equals(libraryName, "libheif", StringComparison.Ordinal))
            {
                // Because the DllImportResolver will be called multiple times we load libheif once
                // and cache the module handle for future requests.
                if (firstRequestForLibHeif)
                {
                    firstRequestForLibHeif = false;
                    cachedLibHeifModule = LoadNativeLibrary(libraryName, assembly, searchPath);
                }

                return cachedLibHeifModule;
            }

            // Fall back to default import resolver.
            return IntPtr.Zero;
        }

        private static nint LoadNativeLibrary(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
        {
            if (OperatingSystem.IsWindows())
            {
                // On Windows the libheif DLL name defaults to heif.dll, so we try to load that if
                // libheif.dll was not found.
                try
                {
                    return NativeLibrary.Load(libraryName, assembly, searchPath);
                }
                catch (DllNotFoundException)
                {
                    if (NativeLibrary.TryLoad("heif.dll", assembly, searchPath, out IntPtr handle))
                    {
                        return handle;
                    }
                    else
                    {
                        throw;
                    }
                }
            }
            else if (OperatingSystem.IsIOS() || OperatingSystem.IsTvOS() || OperatingSystem.IsWatchOS())
            {
                // The Apple mobile/embedded platforms statically link libheif into the AOT compiled main program binary.
                return NativeLibrary.GetMainProgramHandle();
            }
            else
            {
                // Use the default runtime behavior for all other platforms.
                return NativeLibrary.Load(libraryName, assembly, searchPath);
            }
        }
    }
}
