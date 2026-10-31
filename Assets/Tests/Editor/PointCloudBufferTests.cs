using System.Collections.Generic;
using System.Runtime.InteropServices;
using NUnit.Framework;
using Simulator.PointCloud;
using Simulator.PointCloud.Trees;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;

public class PointCloudBufferTests
{
    [StructLayout(LayoutKind.Sequential)]
    struct RusageInfoV2
    {
        public ulong uuid0, uuid1, userTime, systemTime, pkgIdleWkups, interruptWkups, pageins, wiredSize, residentSize,
            physFootprint, procStartAbstime, procExitAbstime, childUserTime, childSystemTime, childPkgIdleWkups,
            childInterruptWkups, childPageins, childElapsedAbstime, diskioBytesRead, diskioBytesWritten;
    }

    [DllImport("/usr/lib/libSystem.B.dylib")]
    static extern int proc_pid_rusage(int pid, int flavor, out RusageInfoV2 info);

    // Physical footprint of this process as macOS memory pressure counts it, Metal allocations included.
    static long FootprintBytes()
    {
        Assert.That(proc_pid_rusage(System.Diagnostics.Process.GetCurrentProcess().Id, 2, out var info), Is.EqualTo(0));
        return (long)info.physFootprint;
    }

    [Test]
    public void RefillingPointBufferKeepsGraphicsMemoryFlat()
    {
        if (SystemInfo.operatingSystemFamily != OperatingSystemFamily.MacOSX)
            Assert.Ignore("Measures the macOS physical footprint.");
        if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
            Assert.Ignore("Requires a graphics device; do not use -nographics.");
        // The node tree renderer refills its point buffers node by node every frame. On Metal every SetData into a
        // buffer kept a copy of the whole buffer, so this loop held about 200 MB and GoMentumDTL tens of gigabytes.
        const int elements = 65536, chunk = 4096, writes = 200;
        var buffer = BufferBuilder.CreatePointBuffer(elements);
        var points = new NativeArray<PointCloudPoint>(chunk, Allocator.Persistent);
        try
        {
            var before = FootprintBytes();
            for (var i = 0; i < writes; i++)
            {
                BufferBuilder.WritePoints(buffer, points, i * chunk % (elements - chunk));
                if (i % 50 == 49)
                    AsyncGPUReadback.Request(buffer, 16, 0).WaitForCompletion();
            }
            var growth = (FootprintBytes() - before) / 1048576.0;
            Assert.That(growth, Is.LessThan(32), $"{writes} writes into a 1 MB point buffer grew the footprint by {growth:F0} MB");
        }
        finally
        {
            buffer.Release();
            points.Dispose();
        }
    }

    [Test]
    public void WrittenPointsArriveInBuffer()
    {
        if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
            Assert.Ignore("Requires a graphics device; do not use -nographics.");
        var buffer = BufferBuilder.CreatePointBuffer(16);
        var first = new NativeArray<PointCloudPoint>(4, Allocator.Persistent);
        var second = new NativeArray<PointCloudPoint>(3, Allocator.Persistent);
        try
        {
            for (var i = 0; i < first.Length; i++)
                first[i] = new PointCloudPoint { Position = new Vector3(i, 1, 2), Color = 0xff000000u | (uint)i };
            for (var i = 0; i < second.Length; i++)
                second[i] = new PointCloudPoint { Position = new Vector3(10 + i, 3, 4), Color = 0x80000000u | (uint)i };
            BufferBuilder.WritePoints(buffer, first, 0);
            BufferBuilder.WritePoints(buffer, second, 4);
            var result = new PointCloudPoint[7];
            buffer.GetData(result, 0, 0, 7);
            for (var i = 0; i < 4; i++)
            {
                Assert.That(result[i].Position, Is.EqualTo(first[i].Position), $"point {i}");
                Assert.That(result[i].Color, Is.EqualTo(first[i].Color), $"point {i}");
            }
            for (var i = 0; i < 3; i++)
            {
                Assert.That(result[4 + i].Position, Is.EqualTo(second[i].Position), $"point {4 + i}");
                Assert.That(result[4 + i].Color, Is.EqualTo(second[i].Color), $"point {4 + i}");
            }
        }
        finally
        {
            buffer.Release();
            first.Dispose();
            second.Dispose();
        }
    }

    [Test]
    public void BufferJustRetiredIsNotRebuiltInNextBuild()
    {
        if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
            Assert.Ignore("Requires a graphics device; do not use -nographics.");
        // Point buffers are written without synchronization with the GPU, so a buffer that frames still in flight
        // may read must not be rewritten right away. With no nodes every build finishes in one step, so each call
        // returns the buffer built during that call.
        var builder = new BufferBuilder(null, 16, 10);
        try
        {
            var ready = new List<ComputeBuffer>();
            for (var i = 0; i < 6; i++)
                ready.Add(builder.GetPopulatedBuffer(new List<string>(), out _));
            for (var k = 1; k + 1 < ready.Count; k++)
                Assert.That(ready[k + 1], Is.Not.SameAs(ready[k - 1]), $"the buffer ready in build {k - 1} was rebuilt in build {k + 1}");
        }
        finally
        {
            builder.Dispose();
        }
    }
}
