using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Rendering;
using UnityEngine;

public static class BuildBundles
{
    [MenuItem("CoreGMIC/Validate Shaders")]
    public static void ValidateAllShadersMenu()
    {
        if (ValidateAllShaders())
        {
            Debug.Log("[CoreGMIC] All shaders and compute shaders validated with 0 errors.");
        }
    }

    public static bool ValidateAllShaders()
    {
        bool ok = true;

        string[] shaderGuids = AssetDatabase.FindAssets("t:Shader", new[] { "Assets/Shaders" });
        foreach (string guid in shaderGuids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            Shader shader = AssetDatabase.LoadAssetAtPath<Shader>(path);
            if (shader == null)
            {
                Debug.LogError($"[CoreGMIC] Failed to load shader asset: {path}");
                ok = false;
                continue;
            }

            ShaderMessage[] messages = ShaderUtil.GetShaderMessages(shader);
            if (messages != null)
            {
                foreach (ShaderMessage msg in messages)
                {
                    if (msg.severity == ShaderCompilerMessageSeverity.Error)
                    {
                        Debug.LogError($"[CoreGMIC] Shader error in {path} ({msg.file}:{msg.line}): {msg.message}");
                        ok = false;
                    }
                    else
                    {
                        Debug.LogWarning($"[CoreGMIC] Shader warning in {path} ({msg.file}:{msg.line}): {msg.message}");
                    }
                }
            }

            if (!shader.isSupported)
            {
                Debug.LogError($"[CoreGMIC] Shader '{shader.name}' ({path}) is not supported on the active graphics API.");
                ok = false;
            }
        }

        string[] computeGuids = AssetDatabase.FindAssets("t:ComputeShader", new[] { "Assets/Shaders" });
        foreach (string guid in computeGuids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            ComputeShader cs = AssetDatabase.LoadAssetAtPath<ComputeShader>(path);
            if (cs == null)
            {
                Debug.LogError($"[CoreGMIC] Failed to load compute shader asset: {path}");
                ok = false;
                continue;
            }

            ShaderMessage[] messages = ShaderUtil.GetComputeShaderMessages(cs);
            if (messages != null)
            {
                foreach (ShaderMessage msg in messages)
                {
                    if (msg.severity == ShaderCompilerMessageSeverity.Error)
                    {
                        Debug.LogError($"[CoreGMIC] Compute error in {path} ({msg.file}:{msg.line}): {msg.message}");
                        ok = false;
                    }
                    else
                    {
                        Debug.LogWarning($"[CoreGMIC] Compute warning in {path} ({msg.file}:{msg.line}): {msg.message}");
                    }
                }
            }
        }

        return ok;
    }

    [MenuItem("CoreGMIC/Build Windows Shader Bundle")]
    public static void BuildWindowsBundle()
    {
        if (!ValidateAllShaders())
        {
            Debug.LogError("[CoreGMIC] Shader validation failed! Aborting AssetBundle build.");
            return;
        }

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