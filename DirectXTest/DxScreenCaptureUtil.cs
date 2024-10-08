﻿using SharpDX;
using SharpDX.Direct3D11;
using SharpDX.DXGI;
using System.Drawing.Imaging;
using Device = SharpDX.Direct3D11.Device;
using MapFlags = SharpDX.Direct3D11.MapFlags;

namespace DirectXTest;

public class DxScreenCaptureUtil
{
    public static (Bitmap? Bitmap, int Rotation) Capture(Rectangle sourceRect)
    {
        var targetScreen = GetTargetScreen(sourceRect);
        if (targetScreen == null) throw new Exception($"Could not find target screen for capture rectangle {sourceRect}");
        // Create DXGI Factory1
        using var factory = new Factory1();
        using var adapter = factory.Adapters.FirstOrDefault(x => x.Outputs.Any(o => o.Description.DeviceName == targetScreen.DeviceName));
        if (adapter == null) throw new Exception($"Could not find adapter for screen {targetScreen.DeviceName}");
        using var device = new Device(adapter); // Create device from Adapter
        Output? output = null; // using (var output = adapter.Outputs.Where(o => o.Description.DeviceName == targetScreen.DeviceName).FirstOrDefault()) // This creates a memory leak!
        for (var i = 0; i < adapter.GetOutputCount(); i++) // I'm open to suggestions here:
        {
            output = adapter.GetOutput(i);
            if (output.Description.DeviceName == targetScreen.DeviceName) break;
            using var _1 = output;
        }

        using var _2 = output;
        if (output == null) throw new Exception($"Could not find output for screen {targetScreen.DeviceName}");

        // This is to instruct client receiving the image to rotate it, seems like a reasonable thing to offload it to client and save a bit of CPU time on server
        int rotation;
        // Width/Height of desktop to capture
        var width = targetScreen.Bounds.Width;
        var height = targetScreen.Bounds.Height;
        var cropRect = sourceRect with { X = sourceRect.X - targetScreen.Bounds.X, Y = sourceRect.Y - targetScreen.Bounds.Y };

        using var output1 = output.QueryInterface<Output1>();
        switch (output1.Description.Rotation)
        {
            case DisplayModeRotation.Rotate90:
                width = targetScreen.Bounds.Height;
                height = targetScreen.Bounds.Width;
                var offsetX = targetScreen.Bounds.X - sourceRect.X;
                cropRect = new Rectangle(
                    sourceRect.Y - targetScreen.Bounds.Y,
                    targetScreen.Bounds.Width - (sourceRect.Width + offsetX),
                    sourceRect.Height, sourceRect.Width);
                rotation = 90;
                break;

            case DisplayModeRotation.Rotate270:
                width = targetScreen.Bounds.Height;
                height = targetScreen.Bounds.Width;
                var offsetY = targetScreen.Bounds.Y - sourceRect.Y;
                cropRect = new Rectangle(
                    targetScreen.Bounds.Height - (sourceRect.Height + offsetY),
                    targetScreen.Bounds.X - sourceRect.X,
                    sourceRect.Height, sourceRect.Width);
                rotation = 270;
                break;

            case DisplayModeRotation.Rotate180:
                rotation = 180;
                break;

            case DisplayModeRotation.Identity:
                rotation = 0;
                break;

            case DisplayModeRotation.Unspecified:
            default:
                throw new ArgumentOutOfRangeException();
        }

        // Create Staging texture CPU-accessible
        var textureDesc = new Texture2DDescription
        {
            CpuAccessFlags = CpuAccessFlags.Read,
            BindFlags = BindFlags.None,
            Format = Format.B8G8R8A8_UNorm,
            Width = width,
            Height = height,
            OptionFlags = ResourceOptionFlags.None,
            MipLevels = 1,
            ArraySize = 1,
            SampleDescription = { Count = 1, Quality = 0 },
            Usage = ResourceUsage.Staging
        };

        using var screenTexture = new Texture2D(device, textureDesc);
        using var duplicatedOutput = output1.DuplicateOutput(device);

        var captureDone = false;
        Bitmap? cropBitmap = null;
        for (var i = 0; !captureDone; i++)
        {
            try
            {
                // Try to get duplicated frame within given time
                duplicatedOutput.TryAcquireNextFrame(1000, out _, out var screenResource);
                using var _3 = screenResource;
                // Ignore first call, this always seems to return a black frame
                if (i == 0) continue;

                // copy resource into memory that can be accessed by the CPU
                using var screenTexture2D = screenResource.QueryInterface<Texture2D>();
                device.ImmediateContext.CopyResource(screenTexture2D, screenTexture);

                // Get the desktop capture texture
                var mapSource = device.ImmediateContext.MapSubresource(screenTexture, 0, MapMode.Read, MapFlags.None);
                var boundsRect = new Rectangle(0, 0, width, height);
                // Create Drawing.Bitmap
                using var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
                // Copy pixels from screen capture Texture to GDI bitmap
                var bitmapData = bitmap.LockBits(boundsRect, ImageLockMode.WriteOnly, bitmap.PixelFormat);
                var sourcePtr = mapSource.DataPointer;
                var destinationPtr = bitmapData.Scan0;
                for (var y = 0; y < height; y++)
                {
                    // Copy a single line
                    Utilities.CopyMemory(destinationPtr, sourcePtr, width * 4);
                    // Advance pointers
                    sourcePtr = IntPtr.Add(sourcePtr, mapSource.RowPitch);
                    destinationPtr = IntPtr.Add(destinationPtr, bitmapData.Stride);
                }

                // Release source and dest locks
                bitmap.UnlockBits(bitmapData);
                device.ImmediateContext.UnmapSubresource(screenTexture, 0);

                // Save the output
                cropBitmap = CropBitmap(bitmap, cropRect);

                // Capture done
                captureDone = true;
            }
            catch (SharpDXException e)
            {
                if (e.ResultCode.Code != SharpDX.DXGI.ResultCode.WaitTimeout.Result.Code) throw;
            }
            finally
            {
                // Dispose manually
                duplicatedOutput.ReleaseFrame();
            }
        }

        return (cropBitmap, rotation);
    }

    /// <summary>
    /// Gets target device (Display) based on the rectangle we want to capture
    /// </summary>
    /// <param name="sourceRect">Rectangle we want to capture</param>
    /// <returns>Screen which contains the area we want to capture or null if no device contains our area of interest</returns>
    private static Screen? GetTargetScreen(Rectangle sourceRect)
    {
        return Screen.AllScreens.FirstOrDefault(scr => sourceRect.X >= scr.Bounds.X && sourceRect.Y >= scr.Bounds.Y && sourceRect.Right <= scr.Bounds.Width + scr.Bounds.X && sourceRect.Bottom <= scr.Bounds.Height + scr.Bounds.Y);
    }

    /// <summary>
    /// Crop bitmap
    /// </summary>
    /// <param name="origin">Original bitmap</param>
    /// <param name="cropRect">Crop rectangle</param>
    /// <returns>Cropped bitmap</returns>
    private static Bitmap CropBitmap(Bitmap origin, Rectangle cropRect)
    {
        var bitmap = new Bitmap(cropRect.Width, cropRect.Height);
        using var g = Graphics.FromImage(bitmap);
        g.DrawImage(origin, -cropRect.X, -cropRect.Y);

        return bitmap;
    }
}