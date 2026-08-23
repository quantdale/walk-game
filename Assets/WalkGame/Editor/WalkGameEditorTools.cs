using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;
using WalkGame.Content;

namespace WalkGame.EditorTools
{
    /// <summary>
    /// One-stop editor tooling: content validation, first-open pipeline/settings setup,
    /// and the iOS Info.plist motion-usage hook. Debug tools are first-class; these keep
    /// the hand-bootstrapped project healthy without manual incantations.
    /// </summary>
    public static class WalkGameEditorTools
    {
        [MenuItem("WalkGame/Validate Content IDs")]
        public static void ValidateContent()
        {
            var catalog = new AshfallBasinCatalog();
            int errors = 0;
            var seenInstances = new HashSet<string>();

            foreach (var instance in catalog.Ashfall.defaultBuildingInstances)
            {
                if (!seenInstances.Add(instance.instanceId))
                {
                    Debug.LogError($"[Validate] Duplicate instance id '{instance.instanceId}'.");
                    errors++;
                }

                if (catalog.GetBuilding(instance.buildingDefinitionId) == null)
                {
                    Debug.LogError($"[Validate] '{instance.instanceId}' references unknown definition '{instance.buildingDefinitionId}'.");
                    errors++;
                }
                else
                {
                    var building = catalog.GetBuilding(instance.buildingDefinitionId);
                    int w = building.footprint.widthCells;
                    int d = building.footprint.depthCells;
                    for (int x = instance.initialPlacement.gridX; x < instance.initialPlacement.gridX + w; x++)
                    {
                        for (int y = instance.initialPlacement.gridY; y < instance.initialPlacement.gridY + d; y++)
                        {
                            if (!catalog.Ashfall.IsInsidePlacementArea(x, y))
                            {
                                Debug.LogError($"[Validate] '{instance.instanceId}' footprint outside area at ({x},{y}).");
                                errors++;
                            }

                            if (catalog.Ashfall.IsReserved(x, y))
                            {
                                Debug.LogError($"[Validate] '{instance.instanceId}' on reserved cells at ({x},{y}).");
                                errors++;
                            }
                        }
                    }
                }
            }

            var projects = catalog.GetProjectsForRegion(AshfallBasinCatalog.RegionId);
            var projectIds = new HashSet<string>();
            foreach (var project in projects)
            {
                projectIds.Add(project.projectId);
            }

            foreach (var project in projects)
            {
                foreach (var prerequisite in project.prerequisiteProjectIds)
                {
                    if (!projectIds.Contains(prerequisite))
                    {
                        Debug.LogError($"[Validate] '{project.projectId}' missing prerequisite '{prerequisite}'.");
                        errors++;
                    }
                }

                foreach (var action in project.rewardActions)
                {
                    if ((action.kind == RewardActionKind.SetBuildingRestored || action.kind == RewardActionKind.UnlockBuilding) &&
                        !seenInstances.Contains(action.targetId))
                    {
                        Debug.LogError($"[Validate] '{project.projectId}' targets unknown instance '{action.targetId}'.");
                        errors++;
                    }
                }
            }

            if (errors == 0)
            {
                Debug.Log($"[Validate] Ashfall Basin OK: {seenInstances.Count} instances, {projects.Count} projects, all references resolve.");
                return;
            }

            throw new InvalidOperationException($"Content validation failed with {errors} error(s).");
        }

        [MenuItem("WalkGame/Setup/Configure URP and Input System")]
        public static void ConfigurePipelineAndInput()
        {
            ConfigureUrp();
            EnableInputSystem();
        }

        /// <summary>
        /// Idempotent batch-mode bring-up for a fresh checkout. Keeping the menu action
        /// and the command-line entry point on the same implementation prevents setup
        /// drift between an agent/CI run and a developer opening the project.
        /// </summary>
        [MenuItem("WalkGame/Setup/Apply All Project Setup")]
        public static void ApplyProjectSetup()
        {
            ConfigurePipelineAndInput();
            ApplyProductIdentity();
            ValidateContent();
            AssetDatabase.SaveAssets();
            Debug.Log("[Setup] Walk Game project setup completed.");
        }

        private static void ConfigureUrp()
        {
            const string assetDir = "Assets/Settings";
            const string assetPath = assetDir + "/URP-HighFidelity.asset";

            var pipeline = AssetDatabase.LoadAssetAtPath<UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset>(assetPath);
            if (pipeline == null)
            {
                Directory.CreateDirectory(assetDir);
                pipeline = ScriptableObject.CreateInstance<UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset>();

                // Mobile-first defaults per TECHNICAL_ARCHITECTURE section 23.
                pipeline.supportsHDR = false;
                pipeline.msaaSampleCount = 4;
                pipeline.renderScale = 1.0f;

                AssetDatabase.CreateAsset(pipeline, assetPath);
                AssetDatabase.SaveAssets();
            }

            GraphicsSettings.defaultRenderPipeline = pipeline;
            QualitySettings.renderPipeline = pipeline;
            Debug.Log($"[Setup] URP assigned: {assetPath}");
        }

        private static void EnableInputSystem()
        {
            var assets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/ProjectSettings.asset");
            if (assets == null || assets.Length == 0)
            {
                Debug.LogWarning("[Setup] ProjectSettings.asset not found.");
                return;
            }

            var serialized = new SerializedObject(assets[0]);
            var handler = serialized.FindProperty("activeInputHandler");
            if (handler != null)
            {
                // 2 = Both (old + Input System) keeps editor convenience without breaking mobile input.
                handler.intValue = 2;
                serialized.ApplyModifiedProperties();
                Debug.Log("[Setup] Active Input Handling set to 'Both'. Restart the editor when prompted.");
            }
        }

        [MenuItem("WalkGame/Setup/Apply Product Identity")]
        public static void ApplyProductIdentity()
        {
            PlayerSettings.companyName = "Walk Game";
            PlayerSettings.productName = "Walk Game";
            PlayerSettings.SetApplicationIdentifier(BuildTargetGroup.Android, "com.quantdale.walkgame");
            PlayerSettings.Android.minimumSdkVersion = AndroidSdkVersions.AndroidApiLevel26;
            PlayerSettings.iOS.appleDeveloperTeamID = string.Empty; // filled by the developer
            Debug.Log("[Setup] Product identity applied (Android package com.quantdale.walkgame).");
        }

        /// <summary>
        /// Reproducible Android development build used by local certification. The
        /// committed Bootstrap scene is the only enabled build scene; optional scenes
        /// stay disabled until they are intentionally part of the slice.
        /// </summary>
        public static void BuildAndroidDevelopment()
        {
            ApplyProjectSetup();

            if (!BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.Android, BuildTarget.Android))
            {
                throw new InvalidOperationException("Android Build Support is not installed for this Unity editor.");
            }

            var enabledScenes = new List<string>();
            foreach (var scene in EditorBuildSettings.scenes)
            {
                if (!scene.enabled)
                {
                    continue;
                }

                if (!File.Exists(scene.path))
                {
                    throw new InvalidOperationException($"Enabled build scene is missing: {scene.path}");
                }

                enabledScenes.Add(scene.path);
            }

            if (enabledScenes.Count == 0)
            {
                throw new InvalidOperationException("No enabled build scenes are configured.");
            }

            const string packageIdentifier = "com.quantdale.walkgame";
            const string outputPath = "Builds/Android/WalkGame-dev.apk";
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath));

            PlayerSettings.SetApplicationIdentifier(BuildTargetGroup.Android, packageIdentifier);
            PlayerSettings.Android.minimumSdkVersion = AndroidSdkVersions.AndroidApiLevel26;
            PlayerSettings.Android.targetSdkVersion = AndroidSdkVersions.AndroidApiLevel35;
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            EditorUserBuildSettings.development = true;
            EditorUserBuildSettings.allowDebugging = true;
            EditorUserBuildSettings.connectProfiler = false;

            if (!EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Android, BuildTarget.Android))
            {
                throw new InvalidOperationException("Could not switch the active build target to Android.");
            }

            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = enabledScenes.ToArray(),
                locationPathName = outputPath,
                target = BuildTarget.Android,
                options = BuildOptions.Development | BuildOptions.AllowDebugging,
            });

            if (report.summary.result != BuildResult.Succeeded || !File.Exists(outputPath))
            {
                throw new InvalidOperationException(
                    $"Android development build failed: {report.summary.result}, errors={report.summary.totalErrors}.");
            }

            Debug.Log($"[Build] Android development APK created: {Path.GetFullPath(outputPath)} ({report.summary.totalSize} bytes).");
        }
    }

    /// <summary>
    /// Adds the motion usage description to every generated Xcode project so permission
    /// prompts satisfy App Store review (PRIVACY_SAFETY_ANTI_CHEAT checklist).
    /// </summary>
    public sealed class IosPedometerPlistPostprocessor : IPostprocessBuildWithReport
    {
        public int callbackOrder => 100;

        public void OnPostprocessBuild(BuildReport report)
        {
            if (report.summary.platform != BuildTarget.iOS)
            {
                return;
            }

            string plistPath = Path.Combine(report.summary.outputPath, "Info.plist");
            if (!File.Exists(plistPath))
            {
                return;
            }

            const string key = "NSMotionUsageDescription";
            string contents = File.ReadAllText(plistPath);
            if (contents.Contains(key))
            {
                return;
            }

            string insertion =
                $"\t<key>{key}</key>\n" +
                "\t<string>Walk Game uses motion data to turn your walking and running into restoration progress in your game world.</string>\n";

            int insertAt = contents.IndexOf("<dict>", System.StringComparison.Ordinal);
            if (insertAt >= 0)
            {
                contents = contents.Insert(insertAt + "<dict>".Length, "\n" + insertion);
                File.WriteAllText(plistPath, contents);
                Debug.Log("[PostBuild] NSMotionUsageDescription added to Info.plist.");
            }
        }
    }
}
