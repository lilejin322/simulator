using System;
using System.Linq;
using System.Runtime.InteropServices;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;

public class MacOSRenderingTests
{
    struct LineData { public Matrix4x4 transform; public Vector3 start, end, color; }
    struct BoxData { public Matrix4x4 transform; public Vector3 position, size, color; }

    [Test]
    public void ExportedPointCloudHasReadableCountsAndCompleteBinaryRecords()
    {
        // The runtime scripts live in the predefined assembly; discover them here
        // so this editor-only test assembly does not alter runtime assembly layout.
        var writerType = AppDomain.CurrentDomain.GetAssemblies()
            .Select(a => a.GetType("Simulator.Utilities.PcdWriter"))
            .First(t => t != null);
        var path = System.IO.Path.GetTempFileName();
        try
        {
            using (var writer = (IDisposable)Activator.CreateInstance(writerType, path))
            {
                writerType.GetMethod("Write").Invoke(writer, new object[] { Vector3.one, 0.5f });
                writerType.GetMethod("Write").Invoke(writer, new object[] { Vector3.zero, 1f });
            }
            var bytes = System.IO.File.ReadAllBytes(path);
            var text = System.Text.Encoding.ASCII.GetString(bytes);
            foreach (var key in new[] { "WIDTH", "POINTS" })
            {
                var count = text.Split('\n').First(l => l.StartsWith(key + " ")).Substring(key.Length).Trim();
                Assert.That(int.Parse(count), Is.EqualTo(2));
            }
            var headerEnd = text.IndexOf("DATA binary\n", StringComparison.Ordinal) + "DATA binary\n".Length;
            Assert.That(bytes.Length - headerEnd, Is.EqualTo(2 * 13));
        }
        finally { System.IO.File.Delete(path); }
    }

    [TestCase("Simulator/Line", 6)]
    [TestCase("Simulator/WireframeBox", 72)]
    [TestCase("Simulator/LidarPointCloud", 6)]
    public void ProceduralVisualizationsProducePixels(string shaderName, int vertices)
    {
        if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
            Assert.Ignore("Requires a graphics device; do not use -nographics.");
        var shader = Shader.Find(shaderName);
        Assert.That(shader, Is.Not.Null, shaderName);
        Assert.That(shader.isSupported, Is.True, shaderName);
        var material = new Material(shader);
        ComputeBuffer buffer = null;
        var target = new RenderTexture(96, 96, 24, RenderTextureFormat.ARGB32);
        var pixels = new Texture2D(96, 96, TextureFormat.RGBA32, false);
        var previous = RenderTexture.active;
        var commands = new CommandBuffer();
        try
        {
            if (shaderName == "Simulator/Line")
            {
                buffer = new ComputeBuffer(1, Marshal.SizeOf<LineData>());
                buffer.SetData(new[] { new LineData { transform = Matrix4x4.identity,
                    start = new Vector3(-0.5f, 0, -3), end = new Vector3(0.5f, 0, -3), color = Vector3.one } });
                material.SetBuffer("_Lines", buffer);
            }
            else if (shaderName == "Simulator/WireframeBox")
            {
                buffer = new ComputeBuffer(1, Marshal.SizeOf<BoxData>());
                buffer.SetData(new[] { new BoxData { transform = Matrix4x4.identity,
                    position = new Vector3(0, 0, -3), size = Vector3.one, color = Vector3.one } });
                material.SetBuffer("_Boxes", buffer);
            }
            else
            {
                buffer = new ComputeBuffer(1, 16);
                buffer.SetData(new[] { new Vector4(0, 0, -3, 1) });
                material.SetBuffer("_PointCloud", buffer);
                material.SetMatrix("_LocalToWorld", Matrix4x4.identity);
                material.SetColor("_Color", Color.white);
                material.SetFloat("_Size", 8);
            }
            material.SetFloat("_LineWidth", 4);
            target.Create();
            commands.SetRenderTarget(target);
            commands.ClearRenderTarget(true, true, Color.black);
            commands.SetViewProjectionMatrices(Matrix4x4.identity,
                GL.GetGPUProjectionMatrix(Matrix4x4.Ortho(-1, 1, -1, 1, 0.1f, 10), true));
            commands.SetGlobalVector("_ScreenParams", new Vector4(96, 96, 1+1f/96, 1+1f/96));
            commands.DrawProcedural(Matrix4x4.identity, material, 0, MeshTopology.Triangles, vertices, 1);
            Graphics.ExecuteCommandBuffer(commands);
            RenderTexture.active = target;
            pixels.ReadPixels(new Rect(0, 0, 96, 96), 0, 0);
            pixels.Apply();
            var lit = pixels.GetPixels32().Count(p => p.r > 100);
            Assert.That(lit, Is.GreaterThan(10), shaderName + " produced no visible geometry");
            Assert.That(lit, Is.LessThan(96 * 96 / 2), shaderName + " filled the viewport unexpectedly");
        }
        finally
        {
            RenderTexture.active = previous;
            buffer?.Release();
            commands.Dispose();
            UnityEngine.Object.DestroyImmediate(material);
            UnityEngine.Object.DestroyImmediate(pixels);
            UnityEngine.Object.DestroyImmediate(target);
        }
    }
}
