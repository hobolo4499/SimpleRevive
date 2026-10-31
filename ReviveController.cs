using UnityEngine;
using UnityEngine.InputSystem;

namespace SimpleRevive;

// Local player: hold H while carrying a dead player's head to pay health and revive them.
internal static class ReviveController
{
    // Reviving costs 10% of your current health, at least 10 (so at 10 or less it kills you).
    // The revived player gets exactly the health that was paid.
    private const float HealthCostFraction = 0.1f;
    internal const int MinHealthCost = 10;
    // Upper bound the host accepts, so a modified client can't send absurd amounts.
    internal const int MaxReviveHealth = 1000;
    private const float HoldSeconds = 2f;

    private static PlayerAvatar? _target;
    private static float _progress;
    // After a revive (or a blocked one), H must be released before starting again.
    private static bool _waitForRelease;

    internal static void LocalTick()
    {
        bool keyHeld = Keyboard.current != null && Keyboard.current.hKey.isPressed && !IsTyping();
        if (!keyHeld)
        {
            _waitForRelease = false;
        }

        PlayerAvatar? target = GetHeldDeadPlayer();
        if (target != _target)
        {
            _target = target;
            _progress = 0f;
        }

        PlayerAvatar me = SemiFunc.PlayerAvatarLocal();
        if (!target || !me)
        {
            _progress = 0f;
            ReviveFeedback.Cancel();
            return;
        }

        ShowText("Hold [H] to revive");

        if (Keyboard.current != null && Keyboard.current.hKey.wasPressedThisFrame)
        {
            SimpleRevive.Logger.LogInfo($"H pressed while holding {target!.playerName}'s head (typing: {IsTyping()}, health: {me.playerHealth.health})");
        }

        if (!keyHeld || _waitForRelease)
        {
            _progress = 0f;
            ReviveFeedback.Cancel();
            return;
        }

        _progress += Time.deltaTime;
        if (_progress < HoldSeconds)
        {
            ReviveFeedback.Charge(_progress / HoldSeconds, HoldSeconds);
            return;
        }

        _progress = 0f;
        _waitForRelease = true;
        ReviveFeedback.Complete(Complete(me, target!));
    }

    // Returns false when the revive was blocked.
    private static bool Complete(PlayerAvatar me, PlayerAvatar target)
    {
        int before = me.playerHealth.health;
        int cost = Mathf.Max(MinHealthCost, Mathf.RoundToInt(before * HealthCostFraction));
        int reviveHealth = Mathf.Min(cost, before);
        me.playerHealth.Hurt(cost, savingGrace: false);

        // Hurt is ignored while invincible (e.g. just revived yourself); no free revives.
        if (me.playerHealth.health == before)
        {
            SimpleRevive.Logger.LogInfo($"Revive blocked: taking damage failed (health {before})");
            return false;
        }

        SimpleRevive.Logger.LogInfo($"Paid {reviveHealth} health (had {before}) to revive {target.playerName} with {reviveHealth} health");
        ReviveNetwork.RequestRevive(target, reviveHealth);
        return true;
    }

    private static PlayerAvatar? GetHeldDeadPlayer()
    {
        PhysGrabber grabber = PhysGrabber.instance;
        if (!grabber || !grabber.grabbed || !grabber.grabbedPhysGrabObject)
        {
            return null;
        }

        PlayerDeathHead head = grabber.grabbedPhysGrabObject.GetComponent<PlayerDeathHead>();
        if (!head || !head.playerAvatar || !head.playerAvatar.isDisabled)
        {
            return null;
        }
        return head.playerAvatar;
    }

    // Same rule the game uses for opening chat: a menu page is often set during normal gameplay,
    // so only the pause and settings pages count as "in a menu".
    private static bool IsTyping()
    {
        if (!SemiFunc.NoTextInputsActive())
        {
            return true;
        }

        MenuPage page = MenuManager.instance ? MenuManager.instance.currentMenuPage : null!;
        return page && (page.menuPageIndex == MenuPageIndex.Escape || page.menuPageIndex == MenuPageIndex.Settings);
    }

    // Uses the small text under the crosshair that items use; it hides itself when not refreshed.
    private static void ShowText(string message)
    {
        if (ItemInfoUI.instance)
        {
            ItemInfoUI.instance.ItemInfoText(null!, message);
        }
    }
}
