using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

public class LensDistortionTests
{
    // The camera renders into an sRGB texture and the distortion kernels copy it into another sRGB texture, so a
    // distortion that moves nothing must leave every byte as it was. Metal encoded the bytes twice: the kernel
    // encodes by hand for the APIs whose storage writes bypass the conversion, and Metal converts on write as well.
    [TestCase(30)]
    [TestCase(128)]
    [TestCase(200)]
    public void IdentityDistortionKeepsSrgbBytes(byte value)
    {
        if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
            Assert.Ignore("Requires a graphics device; do not use -nographics.");
        var shader = AssetDatabase.LoadAssetAtPath<ComputeShader>("Assets/Shaders/LensDistortion.compute");
        Assert.That(shader, Is.Not.Null);
        const int size = 64;
        var source = new Texture2D(size, size, TextureFormat.RGBA32, false, false);
        // Sensor targets are one-slice texture arrays (TextureXR.dimension), which the kernels are compiled for
        var input = new RenderTexture(size, size, 0, GraphicsFormat.R8G8B8A8_SRGB) { dimension = TextureDimension.Tex2DArray, volumeDepth = 1 };
        var output = new RenderTexture(size, size, 0, GraphicsFormat.R8G8B8A8_SRGB) { dimension = TextureDimension.Tex2DArray, volumeDepth = 1, enableRandomWrite = true };
        var cmd = new CommandBuffer();
        try
        {
            var pixels = new Color32[size * size];
            for (var i = 0; i < pixels.Length; i++)
                pixels[i] = new Color32(value, value, value, 255);
            source.SetPixels32(pixels);
            source.Apply(false, false);
            input.Create();
            output.Create();
            Graphics.CopyTexture(source, 0, 0, input, 0, 0);
            var check = AsyncGPUReadback.Request(input, 0, TextureFormat.RGBA32);
            check.WaitForCompletion();
            Assert.That(check.GetData<byte>()[(size / 2 * size + size / 2) * 4], Is.EqualTo(value), "input texture was not filled");

            var kernel = shader.FindKernel("PlumBobDistortion");
            cmd.SetComputeTextureParam(shader, kernel, "_InputTexture", input);
            cmd.SetComputeTextureParam(shader, kernel, "_DistortedTexture", output);
            cmd.SetComputeVectorParam(shader, "_TexSize", new Vector4(size, size, 1f / size, 1f / size));
            cmd.SetComputeFloatParam(shader, "enlargeFactorA", 1f);
            cmd.SetComputeFloatParam(shader, "enlargeFactorB", 0f);
            cmd.SetComputeFloatParam(shader, "frustumWidth", 1f);
            cmd.SetComputeFloatParam(shader, "frustumHeight", 1f);
            foreach (var name in new[] { "b1", "b2", "b3", "b4" })
                cmd.SetComputeFloatParam(shader, name, 0f);
            cmd.DispatchCompute(shader, kernel, size / 8, size / 8, 1);
            Graphics.ExecuteCommandBuffer(cmd);

            var request = AsyncGPUReadback.Request(output, 0, TextureFormat.RGBA32);
            request.WaitForCompletion();
            Assert.That(request.hasError, Is.False);
            var data = request.GetData<byte>();
            var center = (size / 2 * size + size / 2) * 4;
            Assert.That(data[center], Is.EqualTo(value).Within(1), $"red byte {value} came out as {data[center]} (alpha {data[center + 3]}) on {SystemInfo.graphicsDeviceType}");
            Assert.That(data[center + 1], Is.EqualTo(value).Within(1), "green");
            Assert.That(data[center + 2], Is.EqualTo(value).Within(1), "blue");
        }
        finally
        {
            cmd.Release();
            input.Release();
            output.Release();
            Object.DestroyImmediate(input);
            Object.DestroyImmediate(output);
            Object.DestroyImmediate(source);
        }
    }
}
