// PaletteHotBase64 embeds the serialized `_palette_hot` colormap from G'MIC (`gmic_stdlib.gmic`).
// Copyright (C) David Tschumperlé - GREYC UMR CNRS 6072 (https://gmic.eu)
// Dual-licensed under CeCILL-C v1 / CeCILL v2.1 (used under CeCILL v2.1).

using System;
using System.IO;
using System.IO.Compression;
using System.Text;
using UnityEngine;

namespace CoreGMIC.Core
{
    public static class GmicPaletteLoader
    {
        public const string PaletteHotBase64 =
            "MSB1bnNpZ25lZF9jaGFyIGxpdHRsZV9lbmRpYW4KMTI0IDEgMSAzICMxNDEKeJzjFRKXVdLQNTK3dXb3DY6IS87IK66oa+nsmzJz3pKV67fu2n/k1IWrt+4/ff3+66//1AUMpABGdl5BcRlFdZBTndx9g0BOzS2urGvuADl18cr1W3buP3LyPMipr4BO/UdNu0kEjFxC0kpaRtbO3kHRydnFNS3dk2cvXr1lz5EzV+88efP1LwA796yq";

        public static Texture2D CreatePaletteTexture(string cimgBase64, int targetWidth = 256)
        {
            if (string.IsNullOrEmpty(cimgBase64) || targetWidth < 2)
            {
                return null;
            }

            byte[] raw = Convert.FromBase64String(cimgBase64);
            int line1End = Array.IndexOf(raw, (byte)'\n');
            if (line1End < 0)
            {
                return null;
            }

            int line2End = Array.IndexOf(raw, (byte)'\n', line1End + 1);
            if (line2End < 0)
            {
                return null;
            }

            string dimsLine = Encoding.ASCII.GetString(raw, line1End + 1, line2End - line1End - 1);
            string[] parts = dimsLine.Split(new[] { ' ', '#' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 4 ||
                !int.TryParse(parts[0], out int w) ||
                !int.TryParse(parts[1], out int h) ||
                !int.TryParse(parts[2], out int d) ||
                !int.TryParse(parts[3], out int s) ||
                w < 1 || h < 1 || d < 1 || s < 1)
            {
                return null;
            }

            int payloadOffset = line2End + 1;
            if (payloadOffset + 2 >= raw.Length)
            {
                return null;
            }

            // Skip standard 2-byte zlib header (0x78, 0x9C / 0x01 / 0xDA).
            if (raw[payloadOffset] == 0x78)
            {
                payloadOffset += 2;
            }

            int expectedBytes = w * h * d * s;
            byte[] unpacked = new byte[expectedBytes];

            using (MemoryStream ms = new MemoryStream(raw, payloadOffset, raw.Length - payloadOffset))
            using (DeflateStream deflate = new DeflateStream(ms, CompressionMode.Decompress))
            {
                int totalRead = 0;
                while (totalRead < expectedBytes)
                {
                    int read = deflate.Read(unpacked, totalRead, expectedBytes - totalRead);
                    if (read <= 0)
                    {
                        break;
                    }
                    totalRead += read;
                }

                if (totalRead < expectedBytes)
                {
                    return null;
                }
            }

            Texture2D tex = new Texture2D(targetWidth, 1, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };

            Color[] pixels = new Color[targetWidth];
            float inv255 = 1f / 255f;
            int planeStride = w * h * d;

            for (int x = 0; x < targetWidth; x++)
            {
                float fx = (w == 1) ? 0f : (x * (w - 1f) / (targetWidth - 1f));
                int x0 = Mathf.Clamp(Mathf.FloorToInt(fx), 0, w - 1);
                int x1 = Mathf.Min(w - 1, x0 + 1);
                float t = fx - x0;

                float r = Mathf.Round(Mathf.Lerp(unpacked[x0], unpacked[x1], t)) * inv255;
                float g = (s >= 2)
                    ? Mathf.Round(Mathf.Lerp(unpacked[planeStride + x0], unpacked[planeStride + x1], t)) * inv255
                    : r;
                float b = (s >= 3)
                    ? Mathf.Round(Mathf.Lerp(unpacked[planeStride * 2 + x0], unpacked[planeStride * 2 + x1], t)) * inv255
                    : g;

                pixels[x] = new Color(r, g, b, 1f);
            }

            tex.SetPixels(pixels);
            tex.Apply(false, false);
            return tex;
        }
    }
}