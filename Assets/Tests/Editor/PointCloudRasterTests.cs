using NUnit.Framework;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

public class PointCloudRasterTests
{
    struct Point
    {
        public Vector3 Position;
        public uint Color;
    }

    // The solid renderer rasterizes the cloud as MeshTopology.Points and fills the gaps between the one-pixel points
    // itself, so a point must cover one pixel on every graphics API. Metal gave each point a large square when the
    // shader left the point size unset, which turned sparse clouds into flat blocks and LiDAR returns into planes.
    [Test]
    public void SolidRenderPointCoversOnePixel()
    {
        if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
            Assert.Ignore("Requires a graphics device; do not use -nographics.");
        var shader = Shader.Find("Simulator/PointCloud/SolidRender");
        Assert.That(shader, Is.Not.Null);
        const int size = 256, spacing = 16;
        var points = new System.Collections.Generic.List<Point>();
        for (var y = spacing; y < size; y += spacing)
        for (var x = spacing; x < size; x += spacing)
            points.Add(new Point { Position = new Vector3((x + 0.5f) / size * 2 - 1, (y + 0.5f) / size * 2 - 1, 0.5f), Color = 0xffffffffu });
        var material = new Material(shader);
        var buffer = new ComputeBuffer(points.Count, 16);
        var target = new RenderTexture(size, size, 24, GraphicsFormat.R16G16B16A16_SFloat);
        var cmd = new CommandBuffer();
        try
        {
            buffer.SetData(points);
            target.Create();
            cmd.SetGlobalBuffer("_Buffer", buffer);
            cmd.SetGlobalInt("_Colorize", 0);
            cmd.SetGlobalMatrix("_PointCloudMVP", Matrix4x4.identity);  // positions are already clip space
            cmd.SetRenderTarget(target);
            cmd.ClearRenderTarget(true, true, Color.clear);
            cmd.DrawProcedural(Matrix4x4.identity, material, 0, MeshTopology.Points, points.Count);
            Graphics.ExecuteCommandBuffer(cmd);
            var request = AsyncGPUReadback.Request(target, 0, TextureFormat.RGBAFloat);
            request.WaitForCompletion();
            Assert.That(request.hasError, Is.False);
            var data = request.GetData<float>();
            var covered = 0;
            for (var k = 3; k < data.Length; k += 4)
                if (data[k] > 0) covered++;
            Assert.That(covered, Is.EqualTo(points.Count), $"{points.Count} points covered {covered} pixels on {SystemInfo.graphicsDeviceType}");
        }
        finally
        {
            cmd.Release();
            buffer.Release();
            target.Release();
            Object.DestroyImmediate(target);
            Object.DestroyImmediate(material);
        }
    }
}
