using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEngine;

namespace CoreGMIC.Core
{
    public static class ShaderManager
    {
        private static AssetBundle _bundle;
        private static readonly Dictionary<string, Shader> _shaders = new Dictionary<string, Shader>();

        public static void LoadShaders()
        {
            Unload();

            Assembly asm = Assembly.GetExecutingAssembly();
            string resourceName = null;
            foreach (string res in asm.GetManifestResourceNames())
            {
                if (res.EndsWith("win.gmicshaders"))
                {
                    resourceName = res;
                    break;
                }
            }

            if (resourceName == null)
            {
                string resources = string.Join(", ", asm.GetManifestResourceNames());
                CoreGMICMod.Instance?.LogWarn($"Embedded AssetBundle 'win.gmicshaders' was not found. Embedded resources: [{resources}]");
                return;
            }

            byte[] buffer;
            using (Stream stream = asm.GetManifestResourceStream(resourceName))
            {
                if (stream == null)
                {
                    CoreGMICMod.Instance?.LogWarn($"Manifest resource stream '{resourceName}' was null.");
                    return;
                }

                buffer = new byte[stream.Length];
                int totalRead = 0;
                while (totalRead < buffer.Length)
                {
                    int read = stream.Read(buffer, totalRead, buffer.Length - totalRead);
                    if (read == 0)
                    {
                        break;
                    }
                    totalRead += read;
                }
            }

            CoreGMICMod.Instance?.Log($"Read {buffer.Length} bytes from embedded resource '{resourceName}'.");
            _bundle = AssetBundle.LoadFromMemory(buffer);
            if (_bundle == null)
            {
                CoreGMICMod.Instance?.LogError($"AssetBundle.LoadFromMemory failed for '{resourceName}' ({buffer.Length} bytes)!");
                return;
            }

            string[] assetNames = _bundle.GetAllAssetNames();
            CoreGMICMod.Instance?.Log($"Loaded AssetBundle '{resourceName}' with {assetNames.Length} assets: [{string.Join(", ", assetNames)}]");

            Shader[] loaded = _bundle.LoadAllAssets<Shader>();
            if (loaded.Length == 0)
            {
                CoreGMICMod.Instance?.LogWarn($"AssetBundle '{resourceName}' contains 0 Shader assets!");
                return;
            }

            foreach (Shader shader in loaded)
            {
                if (shader != null)
                {
                    _shaders[shader.name] = shader;
                    CoreGMICMod.Instance?.Log($"Loaded shader: {shader.name} (supported: {shader.isSupported})");
                }
            }
        }

        public static Shader GetShader(string shaderName)
        {
            if (_shaders.TryGetValue(shaderName, out Shader shader) && shader != null && shader.isSupported)
            {
                return shader;
            }

            return null;
        }

        public static void Unload()
        {
            _shaders.Clear();

            if (_bundle != null)
            {
                _bundle.Unload(true);
                _bundle = null;
            }
        }
    }
}