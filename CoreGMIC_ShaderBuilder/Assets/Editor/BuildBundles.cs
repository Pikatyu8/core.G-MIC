using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

public static class BuildBundles
{
    [MenuItem("CoreGMIC/Build Windows Shader Bundle")]
    public static void BuildWindowsBundle()
    {
        string[] shaderGuids = AssetDatabase.FindAssets("t:Shader", new[] { "Assets" });
        string[] computeGuids = AssetDatabase.FindAssets("t:ComputeShader", new[] { "Assets" });

        HashSet<string> uniquePaths = new HashSet<string>();
        foreach (string guid in shaderGuids)
        {
            uniquePaths.Add(AssetDatabase.GUIDToAssetPath(guid));
        }
        foreach (string guid in computeGuids)
        {
            uniquePaths.Add(AssetDatabase.GUIDToAssetPath(guid));
        }

        if (uniquePaths.Count == 0)
        {
            Debug.LogError("[CoreGMIC] No .shader or .compute files found in Assets/!");
            return;
        }

        string[] shaderPaths = new string[uniquePaths.Count];
        uniquePaths.CopyTo(shaderPaths);
        foreach (string path in shaderPaths)
        {
            Debug.Log($"[CoreGMIC] Packing shader asset: {path}");
        }

        string outputDir = Path.Combine("Assets", "BundleOutput", "win");
        if (Directory.Exists(outputDir))
        {
            foreach (string file in Directory.GetFiles(outputDir))
            {
                File.Delete(file);
            }
        }
        Directory.CreateDirectory(outputDir);

        AssetBundleBuild[] builds = new AssetBundleBuild[1];
        builds[0] = new AssetBundleBuild
        {
            assetBundleName = "gmicshaders",
            assetNames = shaderPaths
        };

        AssetBundleManifest manifest = BuildPipeline.BuildAssetBundles(
            outputDir,
            builds,
            BuildAssetBundleOptions.ForceRebuildAssetBundle,
            BuildTarget.StandaloneWindows
        );

        if (manifest == null)
        {
            Debug.LogError("[CoreGMIC] BuildAssetBundles failed! Check Console for shader compile errors.");
            return;
        }

        string[] builtBundles = manifest.GetAllAssetBundles();
        Debug.Log($"[CoreGMIC] Built bundles: [{string.Join(", ", builtBundles)}]");

        string bundlePath = Path.Combine(outputDir, "gmicshaders");
        long sizeBytes = File.Exists(bundlePath) ? new FileInfo(bundlePath).Length : 0;
        Debug.Log($"[CoreGMIC] Built gmicshaders successfully ({sizeBytes} bytes).");

        EditorUtility.RevealInFinder(bundlePath);
    }
}