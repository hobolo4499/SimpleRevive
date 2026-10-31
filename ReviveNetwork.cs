using ExitGames.Client.Photon;
using Photon.Pun;
using Photon.Realtime;
using UnityEngine;

namespace SimpleRevive;

// Reviving is host-only in R.E.P.O. (PlayerAvatar.ReviveRPC is master-only), so the reviver asks
// the host, and the host revives the same way the vanilla revive item does.
internal static class ReviveNetwork
{
    // The game uses event codes 123 and 124; SharedUpgrades uses 178.
    private const byte ReviveRequestEventCode = 179;

    internal static void Init()
    {
        PhotonNetwork.NetworkingClient.EventReceived += OnEvent;
    }

    internal static void RequestRevive(PlayerAvatar target, int reviveHealth)
    {
        if (SemiFunc.IsMasterClientOrSingleplayer())
        {
            HostRevive(target, reviveHealth);
            return;
        }

        PhotonNetwork.RaiseEvent(
            ReviveRequestEventCode,
            new object[] { target.photonView.ViewID, reviveHealth },
            new RaiseEventOptions { Receivers = ReceiverGroup.MasterClient },
            SendOptions.SendReliable);
    }

    private static void OnEvent(EventData photonEvent)
    {
        if (photonEvent.Code != ReviveRequestEventCode || !PhotonNetwork.IsMasterClient)
        {
            return;
        }
        if (photonEvent.CustomData is not object[] { Length: 2 } data
            || data[0] is not int viewID
            || data[1] is not int reviveHealth)
        {
            return;
        }

        // The reviver sends the health they paid; keep it within sane bounds.
        reviveHealth = Mathf.Clamp(reviveHealth, 1, ReviveController.MaxReviveHealth);
        HostRevive(SemiFunc.PlayerAvatarGetFromPhotonID(viewID), reviveHealth);
    }

    private static void HostRevive(PlayerAvatar target, int reviveHealth)
    {
        // Someone else may have revived them in the meantime.
        if (!target || !target.isDisabled || !target.playerDeathHead)
        {
            return;
        }

        target.Revive();
        // Revive already restores 1 health.
        if (reviveHealth > 1)
        {
            target.playerHealth.HealOther(reviveHealth - 1, effect: true);
        }
        SimpleRevive.Logger.LogInfo($"Revived {target.playerName} with {reviveHealth} health");
    }
}
