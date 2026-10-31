using NUnit.Framework;
using UnityEditor;
using UnityEngine;

public class SegmentationColorTests
{
    // Linear to sRGB; the GPU applies the same curve when the segmentation pass writes its sRGB target
    static float Encode(float c) => c <= 0.0031308f ? 12.92f * c : 1.055f * Mathf.Pow(c, 1f / 2.4f) - 0.055f;

    // The segmentation colors are stored linear and become 8-bit sRGB on the GPU. A channel that encodes to within
    // 0.1 of a half step would round differently from one GPU to another, as the sky's blue did (237 on Vulkan,
    // 238 on Metal), and a class color must be the same everywhere.
    [Test]
    public void NoSegmentationColorEncodesNearAHalfStep()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Managers/SimulatorManager.prefab");
        Assert.That(prefab, Is.Not.Null);
        var manager = prefab.GetComponent<SimulatorManager>();
        var colors = new System.Collections.Generic.List<(string, Color)> { ("Sky", manager.SkySegmentationColor) };
        foreach (var item in manager.SegmentationColors)
            colors.Add((item.Tag, item.Color));
        Assert.That(colors.Count, Is.GreaterThan(1));
        foreach (var (tag, color) in colors)
        {
            foreach (var (channel, value) in new[] { ("r", color.r), ("g", color.g), ("b", color.b) })
            {
                var encoded = Encode(value) * 255f;
                var fromHalfStep = Mathf.Abs(Mathf.Abs(encoded - Mathf.Floor(encoded)) - 0.5f);
                Assert.That(fromHalfStep, Is.GreaterThanOrEqualTo(0.1f), $"{tag}.{channel} = {value} encodes to {encoded:F3}");
            }
        }
    }
}
