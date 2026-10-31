using System;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace SimpleRevive;

// Local feedback while holding H: a green progress bar under the crosshair, a rising tone while
// charging, and a sound when the revive happens or is cancelled.
internal static class ReviveFeedback
{
    private const int SampleRate = 44100;
    private static readonly Color BackgroundColor = new(0f, 0f, 0f, 0.55f);
    private static readonly Color OutlineColor = new(0.47f, 1f, 0.55f, 0.6f);
    private static readonly Color FillColor = new(0.47f, 1f, 0.55f, 1f); // #78FF8C

    // Root object for the canvas and the audio source; recreated if something destroys it.
    private static GameObject? _root;
    private static GameObject? _bar;
    private static RectTransform? _fill;
    private static AudioSource? _audio;
    private static bool _charging;

    private static AudioClip? _chargeClip;
    private static SoundClip _completeSound;
    private static SoundClip _cancelSound;
    private static bool _soundsLoaded;

    private struct SoundClip
    {
        internal AudioClip Clip;
        internal float Volume;
    }

    // Called every frame while charging; progress is 0..1.
    internal static void Charge(float progress, float holdSeconds)
    {
        if (!EnsureCreated())
        {
            return;
        }

        if (!_charging)
        {
            _charging = true;
            LoadSounds(holdSeconds);
            Play(_chargeClip!, 1f);
        }

        _bar!.SetActive(true);
        _fill!.anchorMax = new Vector2(Mathf.Clamp01(progress), 1f);
    }

    // Called whenever the player isn't charging; only does something right after charging stopped.
    internal static void Cancel()
    {
        if (!_charging)
        {
            return;
        }
        _charging = false;
        Stop();
        if (_cancelSound.Clip)
        {
            Play(_cancelSound.Clip, _cancelSound.Volume);
        }
        HideBar();
    }

    // Called when the hold finished; success is false when the revive was blocked.
    internal static void Complete(bool success)
    {
        _charging = false;
        Stop();
        SoundClip sound = success ? _completeSound : _cancelSound;
        if (sound.Clip)
        {
            Play(sound.Clip, sound.Volume);
        }
        HideBar();
    }

    private static void HideBar()
    {
        if (_bar)
        {
            _bar!.SetActive(false);
        }
    }

    private static bool EnsureCreated()
    {
        if (_root && _bar && _fill && _audio)
        {
            return true;
        }
        if (_root)
        {
            Object.Destroy(_root);
        }

        _root = new GameObject("SimpleRevive Feedback");
        Object.DontDestroyOnLoad(_root);
        _root.hideFlags = HideFlags.HideAndDontSave;

        _audio = _root.AddComponent<AudioSource>();
        _audio.playOnAwake = false;
        _audio.spatialBlend = 0f;

        Canvas canvas = _root.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 500;
        CanvasScaler scaler = _root.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 1f;
        // No GraphicRaycaster, so the overlay never takes mouse input.

        Sprite sprite = WhiteSprite();

        // Outline -> dark background -> fill, each inset a little from its parent.
        RectTransform outline = CreateImage("Bar", _root.transform, sprite, OutlineColor);
        outline.anchorMin = outline.anchorMax = new Vector2(0.5f, 0.5f);
        outline.pivot = new Vector2(0.5f, 0.5f);
        outline.sizeDelta = new Vector2(244f, 18f);
        outline.anchoredPosition = new Vector2(0f, -110f);
        _bar = outline.gameObject;

        RectTransform background = CreateImage("Background", outline, sprite, BackgroundColor);
        Stretch(background, 2f);

        _fill = CreateImage("Fill", background, sprite, FillColor);
        Stretch(_fill, 2f);
        _fill.anchorMax = new Vector2(0f, 1f);

        _bar.SetActive(false);
        return true;
    }

    private static RectTransform CreateImage(string name, Transform parent, Sprite sprite, Color color)
    {
        GameObject go = new(name);
        go.transform.SetParent(parent, false);
        Image image = go.AddComponent<Image>();
        image.sprite = sprite;
        image.color = color;
        image.raycastTarget = false;
        return (RectTransform)go.transform;
    }

    // Fills the parent minus an inset on every side.
    private static void Stretch(RectTransform rect, float inset)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.pivot = new Vector2(0f, 0.5f);
        rect.offsetMin = new Vector2(inset, inset);
        rect.offsetMax = new Vector2(-inset, -inset);
    }

    private static Sprite WhiteSprite()
    {
        Texture2D texture = new(1, 1, TextureFormat.RGBA32, false);
        texture.SetPixel(0, 0, Color.white);
        texture.Apply();
        texture.hideFlags = HideFlags.HideAndDontSave;
        return Sprite.Create(texture, new Rect(0f, 0f, 1f, 1f), new Vector2(0.5f, 0.5f), 1f);
    }

    private static void Play(AudioClip clip, float volume)
    {
        if (!_audio || !clip)
        {
            return;
        }
        // The game's sound mixer group, so the in-game sound volume applies too.
        if (AudioManager.instance && AudioManager.instance.SoundMasterGroup)
        {
            _audio!.outputAudioMixerGroup = AudioManager.instance.SoundMasterGroup;
        }
        _audio!.PlayOneShot(clip, volume * SimpleRevive.SoundVolume.Value);
    }

    private static void Stop()
    {
        if (_audio)
        {
            _audio!.Stop();
        }
    }

    // The charge tone is generated to match the hold time exactly. Completion and cancel use the
    // vanilla revive item's sounds (ItemReviveItem.reviveSound / ReviveChargeUpInterruptedSound)
    // when its prefab can be found, otherwise generated tones.
    private static void LoadSounds(float holdSeconds)
    {
        if (_soundsLoaded)
        {
            return;
        }
        _soundsLoaded = true;

        _chargeClip = ChargeTone(holdSeconds);
        _completeSound = new SoundClip { Clip = ChimeTone(), Volume = 1f };
        _cancelSound = new SoundClip { Clip = CancelTone(), Volume = 1f };

        try
        {
            Component? reviveItem = FindReviveItemPrefab();
            if (!reviveItem)
            {
                SimpleRevive.Logger.LogInfo("Revive item prefab not found; using generated sounds");
                return;
            }
            if (GetSound(reviveItem!, "reviveSound") is { } complete)
            {
                _completeSound = complete;
            }
            if (GetSound(reviveItem!, "ReviveChargeUpInterruptedSound") is { } cancel)
            {
                _cancelSound = cancel;
            }
            SimpleRevive.Logger.LogInfo($"Using revive item sounds: complete {_completeSound.Clip.name}, cancel {_cancelSound.Clip.name}");
        }
        catch (Exception e)
        {
            SimpleRevive.Logger.LogWarning($"Couldn't load revive item sounds, using generated sounds: {e.Message}");
        }
    }

    // ItemReviveItem is newer than some game versions, so it's looked up by name.
    private static Component? FindReviveItemPrefab()
    {
        Type? type = AccessTools.TypeByName("ItemReviveItem");
        if (type == null)
        {
            return null;
        }

        foreach (Object found in Resources.FindObjectsOfTypeAll(type))
        {
            if (found is Component component)
            {
                return component;
            }
        }

        if (!StatsManager.instance)
        {
            return null;
        }
        // Only load prefabs of items that look like the revive item, not the whole item list.
        foreach (var pair in StatsManager.instance.itemDictionary)
        {
            Item item = pair.Value;
            if (!item || (!Contains(pair.Key, "revive") && !Contains(item.name, "revive") && !Contains(item.itemName, "revive")))
            {
                continue;
            }
            GameObject? prefab = GetPrefab(item);
            Component? component = prefab ? prefab!.GetComponentInChildren(type, true) : null;
            if (component)
            {
                return component;
            }
        }
        return null;
    }

    private static bool Contains(string? text, string value)
    {
        return text != null && text.IndexOf(value, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    // Item.prefab is a GameObject in older game versions and a PrefabRef (with .Prefab) in newer ones.
    private static GameObject? GetPrefab(Item item)
    {
        object? value = AccessTools.Field(typeof(Item), "prefab")?.GetValue(item);
        if (value is GameObject gameObject)
        {
            return gameObject;
        }
        return value == null ? null : AccessTools.Property(value.GetType(), "Prefab")?.GetValue(value) as GameObject;
    }

    private static SoundClip? GetSound(Component owner, string fieldName)
    {
        if (AccessTools.Field(owner.GetType(), fieldName)?.GetValue(owner) is not Sound sound
            || sound.Sounds == null || sound.Sounds.Length == 0 || !sound.Sounds[0])
        {
            return null;
        }
        return new SoundClip { Clip = sound.Sounds[0], Volume = Mathf.Clamp(sound.Volume, 0.1f, 1f) };
    }

    // Rising tone that pulses faster as it fills.
    private static AudioClip ChargeTone(float seconds)
    {
        int count = Mathf.CeilToInt(seconds * SampleRate);
        float[] data = new float[count];
        double phase = 0, pulsePhase = 0;
        for (int i = 0; i < count; i++)
        {
            float t = (float)i / SampleRate;
            float k = t / seconds;
            float frequency = Mathf.Lerp(260f, 620f, k * k * (3f - 2f * k));
            float pulseRate = Mathf.Lerp(4f, 14f, k);
            phase += 2.0 * Math.PI * frequency / SampleRate;
            pulsePhase += 2.0 * Math.PI * pulseRate / SampleRate;
            float tone = (float)(Math.Sin(phase) + 0.3 * Math.Sin(2.0 * phase));
            float pulse = 0.7f + 0.3f * (float)Math.Sin(pulsePhase);
            float envelope = Mathf.Clamp01(t / 0.05f) * Mathf.Clamp01((seconds - t) / 0.05f);
            data[i] = tone * pulse * envelope * Mathf.Lerp(0.12f, 0.2f, k);
        }
        return CreateClip("SimpleRevive Charge", data);
    }

    // Three quick rising notes (C5, E5, G5) that ring out.
    private static AudioClip ChimeTone()
    {
        float[] notes = { 523.25f, 659.25f, 783.99f };
        const float noteDelay = 0.09f;
        const float length = 0.9f;
        int count = Mathf.CeilToInt(length * SampleRate);
        float[] data = new float[count];
        for (int n = 0; n < notes.Length; n++)
        {
            int start = Mathf.RoundToInt(n * noteDelay * SampleRate);
            for (int i = start; i < count; i++)
            {
                float t = (float)(i - start) / SampleRate;
                float envelope = Mathf.Clamp01(t / 0.005f) * Mathf.Exp(-t * 5f);
                double phase = 2.0 * Math.PI * notes[n] * t;
                data[i] += (float)(Math.Sin(phase) + 0.25 * Math.Sin(2.0 * phase)) * envelope * 0.18f;
            }
        }
        return CreateClip("SimpleRevive Complete", data);
    }

    // Short falling blip.
    private static AudioClip CancelTone()
    {
        const float length = 0.18f;
        int count = Mathf.CeilToInt(length * SampleRate);
        float[] data = new float[count];
        double phase = 0;
        for (int i = 0; i < count; i++)
        {
            float t = (float)i / SampleRate;
            phase += 2.0 * Math.PI * Mathf.Lerp(420f, 200f, t / length) / SampleRate;
            float envelope = Mathf.Clamp01(t / 0.005f) * (1f - t / length);
            data[i] = (float)Math.Sin(phase) * envelope * 0.18f;
        }
        return CreateClip("SimpleRevive Cancel", data);
    }

    private static AudioClip CreateClip(string name, float[] data)
    {
        AudioClip clip = AudioClip.Create(name, data.Length, 1, SampleRate, false);
        clip.SetData(data, 0);
        clip.hideFlags = HideFlags.HideAndDontSave;
        return clip;
    }
}
