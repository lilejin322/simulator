using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

public class SegmentationSkyColorTests
{
    // The segmentation camera clears its sRGB target with the sky color, so the sky pixels are whatever the GPU
    // makes of that linear color. The color must not encode to a half step, or the sky class comes out one value
    // apart on different GPUs: (219, 226, 237) on Vulkan, (219, 226, 238) on Metal.
    [Test]
    public void SkyClearsToTheSameBytesOnEveryGpu()
    {
        if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
            Assert.Ignore("Requires a graphics device; do not use -nographics.");
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Managers/SimulatorManager.prefab");
        Assert.That(prefab, Is.Not.Null);
        var sky = prefab.GetComponent<SimulatorManager>().SkySegmentationColor;
        var target = new RenderTexture(8, 8, 0, GraphicsFormat.R8G8B8A8_SRGB);
        var cmd = new CommandBuffer();
        try
        {
            target.Create();
            cmd.SetRenderTarget(target);
            cmd.ClearRenderTarget(true, true, sky);
            Graphics.ExecuteCommandBuffer(cmd);
            var request = AsyncGPUReadback.Request(target, 0, TextureFormat.RGBA32);
            request.WaitForCompletion();
            Assert.That(request.hasError, Is.False);
            var data = request.GetData<byte>();
            var got = ((int)data[0], (int)data[1], (int)data[2]);
            Assert.That(got, Is.EqualTo((219, 226, 237)), $"sky cleared to {got} on {SystemInfo.graphicsDeviceType}");
        }
        finally
        {
            cmd.Release();
            target.Release();
            Object.DestroyImmediate(target);
        }
    }
}
