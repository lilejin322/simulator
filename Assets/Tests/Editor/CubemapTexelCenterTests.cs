using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

public class CubemapTexelCenterTests
{
    const int Size = 64;

    // A cubemap whose texels say where they are: (column, row, face).
    static Cubemap LabeledCube()
    {
        var cube = new Cubemap(Size, TextureFormat.RGBAFloat, false) { filterMode = FilterMode.Point };
        for (var face = 0; face < 6; face++)
        {
            var pixels = new Color[Size * Size];
            for (var y = 0; y < Size; y++)
            for (var x = 0; x < Size; x++)
                pixels[y * Size + x] = new Color(x, y, face, 1);
            cube.SetPixels(pixels, (CubemapFace)face);
        }
        cube.Apply(false);
        return cube;
    }

    static (Vector4[] raw, Vector4[] centered) Sample(IList<Vector3> directions)
    {
        var shader = AssetDatabase.LoadAssetAtPath<ComputeShader>("Assets/Tests/Editor/CubemapTexelProbe.compute");
        Assert.That(shader, Is.Not.Null);
        var cube = LabeledCube();
        var input = new ComputeBuffer(directions.Count, 12);
        var raw = new ComputeBuffer(directions.Count, 16);
        var centered = new ComputeBuffer(directions.Count, 16);
        try
        {
            input.SetData(new List<Vector3>(directions));
            var kernel = shader.FindKernel("Probe");
            shader.SetTexture(kernel, "_Cube", cube);
            shader.SetBuffer(kernel, "_Directions", input);
            shader.SetBuffer(kernel, "_Raw", raw);
            shader.SetBuffer(kernel, "_Centered", centered);
            shader.SetInt("_Count", directions.Count);
            shader.SetFloat("_FaceSize", Size);
            shader.Dispatch(kernel, (directions.Count + 63) / 64, 1, 1);
            var a = new Vector4[directions.Count];
            var b = new Vector4[directions.Count];
            raw.GetData(a);
            centered.GetData(b);
            return (a, b);
        }
        finally
        {
            input.Release();
            raw.Release();
            centered.Release();
            Object.DestroyImmediate(cube);
        }
    }

    [Test]
    public void DirectionsOnARowBoundaryReadTheSameTexel()
    {
        if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
            Assert.Ignore("Requires a graphics device; do not use -nographics.");
        // Horizontal beams: a hair above and a hair below the center row of each side face, at several azimuths
        var directions = new List<Vector3>();
        foreach (var delta in new[] { 1e-7f, -1e-7f })
        for (var k = 0; k < 16; k++)
        {
            var t = -0.9f + 1.8f * k / 15f;
            directions.Add(new Vector3(1, delta, t));
            directions.Add(new Vector3(-1, delta, t));
            directions.Add(new Vector3(t, delta, 1));
            directions.Add(new Vector3(t, delta, -1));
        }
        var (_, centered) = Sample(directions);
        var half = directions.Count / 2;
        var differing = 0;
        for (var i = 0; i < half; i++)
            if (centered[i] != centered[i + half]) differing++;
        Assert.That(differing, Is.Zero, $"{differing} of {half} boundary pairs read different texels on {SystemInfo.graphicsDeviceType}");
    }

    [Test]
    public void DirectionsInsideATexelKeepTheirTexel()
    {
        if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
            Assert.Ignore("Requires a graphics device; do not use -nographics.");
        // Random directions, well inside the texel they land in (at least a tenth of a texel from any boundary)
        var random = new System.Random(7);
        var directions = new List<Vector3>();
        for (var i = 0; i < 4096; i++)
        {
            var u = (float)((random.Next(Size) + 0.1 + 0.8 * random.NextDouble()) / Size) * 2 - 1;
            var v = (float)((random.Next(Size) + 0.1 + 0.8 * random.NextDouble()) / Size) * 2 - 1;
            var sign = random.Next(2) == 0 ? 1f : -1f;
            switch (random.Next(3))
            {
                case 0: directions.Add(new Vector3(sign, v, u)); break;
                case 1: directions.Add(new Vector3(u, sign, v)); break;
                default: directions.Add(new Vector3(u, v, sign)); break;
            }
        }
        var (raw, centered) = Sample(directions);
        var differing = 0;
        for (var i = 0; i < directions.Count; i++)
            if (raw[i] != centered[i]) differing++;
        Assert.That(differing, Is.Zero, $"{differing} of {directions.Count} interior directions changed texel");
    }
}

public class HorizontalBeamRowTests
{
    // A LiDAR beam at exactly 0 degrees elevation points at the boundary between the two center rows of a face:
    // Vulkan reads the row below it and Metal the row above. Centered, every GPU reads the row below, like Linux.
    [Test]
    public void HorizontalBeamReadsTheRowBelowTheCenterLine()
    {
        if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
            Assert.Ignore("Requires a graphics device; do not use -nographics.");
        const int size = 64, count = 64;
        var shader = AssetDatabase.LoadAssetAtPath<ComputeShader>("Assets/Tests/Editor/CubemapTexelProbe.compute");
        var cube = new Cubemap(size, TextureFormat.RGBAFloat, false) { filterMode = FilterMode.Point };
        for (var face = 0; face < 6; face++)
        {
            var pixels = new Color[size * size];
            for (var y = 0; y < size; y++)
            for (var x = 0; x < size; x++)
                pixels[y * size + x] = new Color(x, y, face, 1);
            cube.SetPixels(pixels, (CubemapFace)face);
        }
        cube.Apply(false);
        var raw = new ComputeBuffer(count, 16);
        var centered = new ComputeBuffer(count, 16);
        var ys = new ComputeBuffer(count, 4);
        try
        {
            var kernel = shader.FindKernel("HorizontalBeam");
            shader.SetTexture(kernel, "_Cube", cube);
            shader.SetBuffer(kernel, "_BeamRaw", raw);
            shader.SetBuffer(kernel, "_BeamCentered", centered);
            shader.SetBuffer(kernel, "_BeamY", ys);
            shader.SetInt("_Count", count);
            shader.SetFloat("_FaceSize", size);
            shader.Dispatch(kernel, 1, 1, 1);
            var a = new Vector4[count]; var b = new Vector4[count]; var y = new float[count];
            raw.GetData(a); centered.GetData(b); ys.GetData(y);
            var rawRows = new System.Collections.Generic.SortedDictionary<int, int>();
            var centeredRows = new System.Collections.Generic.SortedDictionary<int, int>();
            for (var i = 0; i < count; i++)
            {
                rawRows[(int)a[i].y] = rawRows.TryGetValue((int)a[i].y, out var n) ? n + 1 : 1;
                centeredRows[(int)b[i].y] = centeredRows.TryGetValue((int)b[i].y, out var m) ? m + 1 : 1;
            }
            Debug.Log($"BEAMROW {SystemInfo.graphicsDeviceType}: direction.y = {y[0]:E3} (cos(pi/2)); raw rows {string.Join(",", rawRows)}; centered rows {string.Join(",", centeredRows)} of {size}");
            Assert.That(centeredRows.Keys, Is.EqualTo(new[] { size / 2 }), $"centered rows {string.Join(",", centeredRows)} on {SystemInfo.graphicsDeviceType}");
        }
        finally
        {
            raw.Release(); centered.Release(); ys.Release();
            Object.DestroyImmediate(cube);
        }
    }
}
