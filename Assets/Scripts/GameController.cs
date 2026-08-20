using UnityEngine;
using UnityEngine.InputSystem; // Added New Input System namespace
using Mirror;

public class GameController : NetworkBehaviour
{
    // Synced so every peer's GameResultPopup pops up together the moment the server decides the
    // game is over - see OnResultChanged.
    [SyncVar(hook = nameof(OnResultChanged))] private GameResult result = GameResult.None;

    void Update()
    {
        if (!NetworkServer.active || !isServer) return;

        // New Input System check for Spacebar. This is a host-only debug shortcut, so it can
        // only ever spawn from a base the host owns - same rule CmdRequestSpawn enforces for clients.
        PlayerBase selectedBase = BaseSelectionManager.SelectedBase;
        if (Keyboard.current != null && Keyboard.current.spaceKey.wasPressedThisFrame
            && selectedBase != null && selectedBase.Owner == BaseOwner.Host)
        {
            selectedBase.ServerTrySpawn();
        }
    }

    public override void OnStartServer()
    {
        base.OnStartServer();
        InvokeRepeating(nameof(CheckGameEnd), 1f, 1f);
    }

    // Players win once every BotBase is destroyed; bots win once every Queen has fallen. BotBase is
    // a scene-placed NetworkIdentity so a "dead" one stays deactivated rather than truly destroyed
    // (see CLAUDE.md) - FindObjectsByType with FindObjectsInactive.Include is what lets this see
    // those too, rather than relying on a registry that would lose track of them.
    [Server]
    private void CheckGameEnd()
    {
        if (result != GameResult.None) return;

        // A base is out either when its Queen falls or when its last Builder does - with nobody left
        // to carry food to her, a Queen can never produce again, so it's the same loss either way.
        var playerBases = BaseSelectionManager.AllBases;
        bool allBasesLost = playerBases.Count > 0;
        foreach (PlayerBase b in playerBases)
        {
            if (b.IsQueenAlive && b.HasLivingBuilder) { allBasesLost = false; break; }
        }

        BotBase[] botBases = FindObjectsByType<BotBase>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        bool allBotBasesDead = botBases.Length > 0;
        foreach (BotBase b in botBases)
        {
            if (b.IsAlive) { allBotBasesDead = false; break; }
        }

        if (allBotBasesDead)
        {
            CancelInvoke(nameof(CheckGameEnd));
            result = GameResult.PlayersWon;
        }
        else if (allBasesLost)
        {
            CancelInvoke(nameof(CheckGameEnd));
            result = GameResult.BotsWon;
        }
    }

    private void OnResultChanged(GameResult oldValue, GameResult newValue)
    {
        if (newValue == GameResult.None) return;
        GameResultPopup.Show(newValue);
    }
}
