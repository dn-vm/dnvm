// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

#nullable disable

using System;
using System.IO;

namespace Dnvm.Test;

internal readonly record struct TempDirectory(string Path) : IDisposable
{
    public static TempDirectory CreateSubDirectory(string basePath)
    {
        while (true)
        {
            string dir = System.IO.Path.Combine(basePath, Guid.NewGuid().ToString());
            try
            {
                Directory.CreateDirectory(dir);
                return new TempDirectory(dir);
            }
            catch (IOException)
            {
                // retry
            }
        }
    }

    public TempDirectory CreateSubDirectory() => CreateSubDirectory(Path);

    public void Dispose()
    {
        if (Path != null && Directory.Exists(Path))
        {
            try
            {
                Directory.Delete(Path, recursive: true);
            }
            catch
            {
            }
        }
    }
}