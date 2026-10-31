/**
 * Copyright (c) 2021 LG Electronics, Inc.
 *
 * This software contains code licensed as described in LICENSE.
 *
 */

namespace UnityEditor.Rendering.HighDefinition.ShaderGraph
{
    using UnityEditor.ShaderGraph;

    static class SVLShaderPasses
    {
        // Same includes as HDShaderPasses.DepthOnlyIncludes, with the sensor pass in place of the depth-only pass
        static IncludeCollection GenerateIncludes(string passInclude)
        {
            return new IncludeCollection
            {
                { CoreIncludes.CorePregraph },
                { CoreIncludes.kNormalSurfaceGradient, IncludeLocation.Pregraph },
                { CoreIncludes.kPassPlaceholder, IncludeLocation.Pregraph },
                { CoreIncludes.CoreUtility },
                { CoreIncludes.kDecalUtilities, IncludeLocation.Pregraph },
                { CoreIncludes.kPostDecalsPlaceholder, IncludeLocation.Pregraph },
                { CoreIncludes.kShaderGraphFunctions, IncludeLocation.Pregraph },
                { passInclude, IncludeLocation.Postgraph }
            };
        }

        // Sensor passes only provide a plain vertex/fragment program, so no VFX or tessellation variants
        static StructCollection Structs => HDShaderPasses.GenerateStructs(null, false, false);

        static PragmaCollection Pragmas => HDShaderPasses.GeneratePragmas(CorePragmas.DotsInstanced, false, false);

        // The passes clip transparent surfaces below half opacity (Assets/Shaders/*Pass.hlsl), as HDRP 10 Shader Graphs did
        // with their surface type define. HDRP 17 only declares the transparent keyword for its own passes.
        static KeywordCollection Keywords => new KeywordCollection { CoreKeywordDescriptors.SurfaceTypeTransparent };

        public static PassDescriptor GenerateLidar()
        {
            return new PassDescriptor
            {
                displayName = "SimulatorLidarPass",
                // Sensor passes don't light anything, so they go through the depth-only code paths
                referenceName = "SHADERPASS_DEPTH_ONLY",
                lightMode = "SimulatorLidarPass",
                useInPreview = false,
                validPixelBlocks = new[]
                {
                    BlockFields.SurfaceDescription.BaseColor,
                    BlockFields.SurfaceDescription.Metallic,
                    BlockFields.SurfaceDescription.Alpha,
                    BlockFields.SurfaceDescription.AlphaClipThreshold,
                    HDBlockFields.SurfaceDescription.DepthOffset
                },
                validVertexBlocks = new[]
                {
                    BlockFields.VertexDescription.Position
                },
                structs = Structs,
                requiredFields = CoreRequiredFields.BasicSurfaceData,
                pragmas = Pragmas,
                defines = new DefineCollection()
                {
                    { new KeywordDescriptor()
                    {
                        displayName = "Mask Map",
                        referenceName = "_MASKMAP",
                        type = KeywordType.Boolean,
                        definition = KeywordDefinition.Predefined,
                        scope = KeywordScope.Local,
                    }, 0 }
                },
                keywords = Keywords,
                includes = GenerateIncludes("Assets/Shaders/LidarPass.hlsl")
            };
        }

        public static PassDescriptor GenerateSegmentation()
        {
            return new PassDescriptor
            {
                displayName = "SimulatorSegmentationPass",
                // Sensor passes don't light anything, so they go through the depth-only code paths
                referenceName = "SHADERPASS_DEPTH_ONLY",
                lightMode = "SimulatorSegmentationPass",
                useInPreview = false,
                validPixelBlocks = new[]
                {
                    BlockFields.SurfaceDescription.BaseColor,
                    BlockFields.SurfaceDescription.Alpha,
                    BlockFields.SurfaceDescription.AlphaClipThreshold,
                    HDBlockFields.SurfaceDescription.DepthOffset
                },
                validVertexBlocks = new[]
                {
                    BlockFields.VertexDescription.Position
                },
                structs = Structs,
                requiredFields = CoreRequiredFields.BasicSurfaceData,
                pragmas = Pragmas,
                defines = new DefineCollection(),
                keywords = Keywords,
                includes = GenerateIncludes("Assets/Shaders/SegmentationPass.hlsl")
            };
        }

        public static PassDescriptor GenerateDepth()
        {
            return new PassDescriptor
            {
                displayName = "SimulatorDepthPass",
                // Sensor passes don't light anything, so they go through the depth-only code paths
                referenceName = "SHADERPASS_DEPTH_ONLY",
                lightMode = "SimulatorDepthPass",
                useInPreview = false,
                validPixelBlocks = new[]
                {
                    BlockFields.SurfaceDescription.BaseColor,
                    BlockFields.SurfaceDescription.Alpha,
                    BlockFields.SurfaceDescription.AlphaClipThreshold,
                    HDBlockFields.SurfaceDescription.DepthOffset
                },
                validVertexBlocks = new[]
                {
                    BlockFields.VertexDescription.Position
                },
                structs = Structs,
                requiredFields = CoreRequiredFields.BasicSurfaceData,
                pragmas = Pragmas,
                defines = new DefineCollection(),
                keywords = Keywords,
                includes = GenerateIncludes("Assets/Shaders/DepthPass.hlsl")
            };
        }
    }
}
