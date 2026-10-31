using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace SimpleRevive;

[BepInPlugin("SolEclips.SimpleRevive", "SimpleRevive", "1.0.4")]
public class SimpleRevive : BaseUnityPlugin
{
    internal static SimpleRevive Instance { get; private set; } = null!;
    internal new static ManualLogSource Logger => Instance._logger;
    private ManualLogSource _logger => base.Logger;
    internal Harmony? Harmony { get; set; }
    internal static ConfigEntry<float> SoundVolume = null!;

    private void Awake()
    {
        Instance = this;
        
        // Prevent the plugin from being deleted
        this.gameObject.transform.parent = null;
        this.gameObject.hideFlags = HideFlags.HideAndDontSave;

        SoundVolume = Config.Bind("General", "SoundVolume", 0.6f,
            new ConfigDescription("Volume of the revive sounds (0 = off)", new AcceptableValueRange<float>(0f, 1f)));

        Patch();
        ReviveNetwork.Init();

        Logger.LogInfo($"{Info.Metadata.GUID} v{Info.Metadata.Version} has loaded!");
    }

    internal void Patch()
    {
        Harmony ??= new Harmony(Info.Metadata.GUID);
        Harmony.PatchAll();
    }

    internal void Unpatch()
    {
        Harmony?.UnpatchSelf();
    }

    private void Update()
    {
        ReviveController.LocalTick();
    }
}