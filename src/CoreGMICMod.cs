using System.Reflection;
using CoreGMIC.Core;
using CoreGMIC.Heavy;
using CoreGMIC.LUT;
using CoreGMIC.UI;
using Modding;
using UnityEngine;

namespace CoreGMIC
{
    public class CoreGMICMod : Mod, ICustomMenuMod, IGlobalSettings<ModSettings>
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

            ShaderManager.LoadShaders();
            RegisterBuiltInFilters();
            FilterManager.InitializeAll();
            FilterManager.IsEnabled = Settings.Enabled;
            FilterManager.SetActiveById(Settings.SelectedFilterId);
            EntityExclusionManager.Initialize(Settings);

            On.GameCameras.SceneInit += OnGameCamerasSceneInit;
            AttachBridgeToMainCamera();
            ModMenu.RefreshFromManager();
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
            FilterManager.Register(new SinLutFilter());
            FilterManager.Register(new Pixelate3x3Filter());
            FilterManager.Register(new QuadtreeVariationsFilter());
            FilterManager.Register(new BarbouillagePaintDaubFilter());
            FilterManager.Register(new ChalkItUpFilter());
        }

        private void OnGameCamerasSceneInit(On.GameCameras.orig_SceneInit orig, GameCameras self)
        {
            orig(self);
            EntityExclusionManager.RefreshAll();
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
    }
}