using System.Reflection;
using CoreGMIC.Core;
using CoreGMIC.UI;
using Modding;
using UnityEngine;

namespace CoreGMIC
{
    public class CoreGMICMod : Mod, ITogglableMod, ICustomMenuMod, IGlobalSettings<ModSettings>
    {
        public static CoreGMICMod Instance { get; private set; }
        public static ModSettings Settings { get; private set; } = new ModSettings();

        public bool ToggleButtonInsideMenu => false;

        public CoreGMICMod() : base("core.G'MIC")
        {
            Instance = this;
        }

        public override string GetVersion()
        {
            return Assembly.GetExecutingAssembly().GetName().Version.ToString();
        }

        public override void Initialize()
        {
            Log("Initializing core.G'MIC post-processing bridge...");

            RegisterBuiltInFilters();
            FilterManager.InitializeAll();
            FilterManager.IsEnabled = Settings.Enabled;
            FilterManager.SetActiveById(Settings.SelectedFilterId);

            On.GameCameras.SceneInit += OnGameCamerasSceneInit;
            AttachBridgeToMainCamera();
        }

        public void Unload()
        {
            Log("Unloading core.G'MIC...");

            On.GameCameras.SceneInit -= OnGameCamerasSceneInit;
            DetachBridgeFromMainCamera();
            FilterManager.DisposeAll();
        }

        public void OnLoadGlobal(ModSettings s)
        {
            Settings = s ?? new ModSettings();
        }

        public ModSettings OnSaveGlobal()
        {
            return Settings;
        }

        public MenuScreen GetMenuScreen(MenuScreen modListMenu, ModToggleDelegates? toggleDelegates)
        {
            return ModMenu.GetMenuScreen(modListMenu);
        }

        private void RegisterBuiltInFilters()
        {
            // Test and ported filters will be registered here in Step 3+.
        }

        private void OnGameCamerasSceneInit(On.GameCameras.orig_SceneInit orig, GameCameras self)
        {
            orig(self);
            AttachBridgeToMainCamera(self);
        }

        private void AttachBridgeToMainCamera(GameCameras cameras = null)
        {
            cameras ??= GameCameras.instance;
            if (cameras == null || cameras.mainCamera == null)
            {
                return;
            }

            GameObject camObj = cameras.mainCamera.gameObject;
            if (camObj.GetComponent<PostProcessBridge>() == null)
            {
                camObj.AddComponent<PostProcessBridge>();
                Log("Attached PostProcessBridge to mainCamera.");
            }
        }

        private void DetachBridgeFromMainCamera()
        {
            if (GameCameras.instance == null || GameCameras.instance.mainCamera == null)
            {
                return;
            }

            PostProcessBridge bridge = GameCameras.instance.mainCamera.GetComponent<PostProcessBridge>();
            if (bridge != null)
            {
                Object.Destroy(bridge);
                Log("Detached PostProcessBridge from mainCamera.");
            }
        }
    }
}