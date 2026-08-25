using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.UI;

// One of the tile action buttons. Clicking it arms a client-local mode where clicking a tile inside
// the viewed base does something to it: build a magazine on it, have a Builder dig an obstacle out,
// or have one fill a floor tile in. While armed it draws the interior grid, a green/red hover
// highlight (green when the action is allowed on the tile under the cursor) and a marker on every
// tile already waiting for a Builder. Right-click, Escape, or clicking the button again cancel.
//
// All three buttons are this same component, told apart by their `action` - and they share which one
// is armed, so only ever one thing can happen when a tile is clicked. Building is instant and
// disarms; digging and filling only place an order, so they stay armed and several tiles can be
// marked in a row while the markers show what's still outstanding.
public class TileActionButton : MonoBehaviour
{
    [SerializeField] private Button button;
    [SerializeField] private TMP_Text label;
    [Tooltip("What clicking a tile does while this button is armed.")]
    [SerializeField] private TileAction action = TileAction.Build;

    private static Material gridMaterial;

    // Which button is currently armed, if any. Client-local and unsynced, like the rest of the
    // viewing state - see BaseSelectionManager.
    private static TileActionButton armed;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetArmed()
    {
        armed = null;
    }

    private void Awake()
    {
        button.onClick.AddListener(OnClicked);
    }

    // The project renders via URP, which never calls the legacy OnRenderObject callback - this
    // event is URP's equivalent hook for issuing GL calls after a camera's done rendering.
    private void OnEnable()
    {
        RenderPipelineManager.endCameraRendering += OnEndCameraRendering;
    }

    private void OnDisable()
    {
        RenderPipelineManager.endCameraRendering -= OnEndCameraRendering;

        if (armed == this) armed = null;
    }

    private void Update()
    {
        PlayerBase viewedBase = ViewedBase();
        bool usable = viewedBase != null && viewedBase.IsOwnedByLocalPlayer && IsAvailableOn(viewedBase);

        if (armed == this && !usable) armed = null;

        button.interactable = usable;
        label.text = armed == this ? CancelName : ActionName;

        if (armed == this) HandleInput(viewedBase);
    }

    private static PlayerBase ViewedBase()
    {
        return ViewManager.CurrentView == ViewMode.Base ? ViewManager.ViewedBase : null;
    }

    // The three buttons label themselves from their action rather than needing a string wired in.
    private string ActionName
    {
        get
        {
            switch (action)
            {
                case TileAction.Dig: return "Destroy";
                case TileAction.Fill: return "Fill";
                default: return "New Build";
            }
        }
    }

    private string CancelName
    {
        get
        {
            switch (action)
            {
                case TileAction.Dig: return "Cancel Destroy";
                case TileAction.Fill: return "Cancel Fill";
                default: return "Cancel Build";
            }
        }
    }

    // Building needs a magazine prefab to put up; digging and filling only ever produce floor and
    // obstacle tiles, which every base has.
    private bool IsAvailableOn(PlayerBase viewedBase)
    {
        return action != TileAction.Build || viewedBase.CanBuildMagazine;
    }

    // PlayerBase decides in both places - here for the preview, and again server-side as the real
    // check - so the two can never disagree.
    private bool IsAllowedOn(PlayerBase viewedBase, Vector2Int tile)
    {
        switch (action)
        {
            case TileAction.Dig: return viewedBase.CanOrderTileWork(tile, fill: false);
            case TileAction.Fill: return viewedBase.CanOrderTileWork(tile, fill: true);
            default: return viewedBase.IsTileBuildable(tile);
        }
    }

    private void HandleInput(PlayerBase viewedBase)
    {
        if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
        {
            armed = null;
            return;
        }

        Mouse mouse = Mouse.current;
        if (mouse == null || Camera.main == null) return;

        if (mouse.rightButton.wasPressedThisFrame)
        {
            armed = null;
            return;
        }

        if (!mouse.leftButton.wasPressedThisFrame) return;
        if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()) return;

        if (!TryHoveredTile(viewedBase, out Vector2Int tile)) return;
        if (!IsAllowedOn(viewedBase, tile)) return;

        if (action == TileAction.Build)
        {
            viewedBase.CmdBuildTile(tile, TileType.Magazine);

            // Instant, so there's nothing left to keep the mode up for.
            armed = null;
            return;
        }

        // Digging and filling are orders a Builder carries out, so the mode stays up: mark as many
        // tiles as you like and watch the markers clear as they're worked.
        viewedBase.CmdOrderTileWork(tile, action == TileAction.Fill);
    }

    // Which tile the cursor is over, if it's over the room at all. WorldToTile clamps to the grid,
    // so without the bounds check a click anywhere outside would land on the nearest wall tile.
    private static bool TryHoveredTile(PlayerBase viewedBase, out Vector2Int tile)
    {
        tile = default;
        if (Mouse.current == null || Camera.main == null) return false;

        Vector3 worldPoint = Camera.main.ScreenToWorldPoint(Mouse.current.position.ReadValue());
        worldPoint.z = 0f;
        if (!viewedBase.ContainsInterior(worldPoint)) return false;

        tile = viewedBase.WorldToTile(worldPoint);
        return true;
    }

    private void OnClicked()
    {
        PlayerBase viewedBase = ViewedBase();
        if (viewedBase == null || !viewedBase.IsOwnedByLocalPlayer || !IsAvailableOn(viewedBase)) return;

        armed = armed == this ? null : this;
    }

    private void OnEndCameraRendering(ScriptableRenderContext context, Camera camera)
    {
        if (armed != this || camera != Camera.main || ViewManager.CurrentView != ViewMode.Base) return;

        PlayerBase viewedBase = ViewManager.ViewedBase;
        if (viewedBase == null) return;

        EnsureGridMaterial();

        // OnRenderObject sets these up implicitly for the rendering camera; hooking in via URP's
        // callback instead means doing it by hand so the GL calls land in the right place.
        GL.PushMatrix();
        GL.LoadProjectionMatrix(camera.projectionMatrix);
        GL.modelview = camera.worldToCameraMatrix;
        gridMaterial.SetPass(0);

        DrawGridLines(viewedBase);
        DrawPendingJobs(viewedBase);
        DrawHoverHighlight(viewedBase);

        GL.PopMatrix();
    }

    private static void EnsureGridMaterial()
    {
        if (gridMaterial != null) return;

        gridMaterial = new Material(Shader.Find("Hidden/Internal-Colored"));
        gridMaterial.hideFlags = HideFlags.HideAndDontSave;
        gridMaterial.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        gridMaterial.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        gridMaterial.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Off);
        gridMaterial.SetInt("_ZWrite", 0);
    }

    private static void DrawGridLines(PlayerBase viewedBase)
    {
        Vector3 origin = viewedBase.GridOrigin;
        float tileSize = viewedBase.TileSize;
        int columns = viewedBase.GridColumns;
        int rows = viewedBase.GridRows;

        GL.Begin(GL.LINES);
        GL.Color(new Color(1f, 1f, 1f, 0.35f));

        for (int x = 0; x <= columns; x++)
        {
            float worldX = origin.x + x * tileSize;
            GL.Vertex3(worldX, origin.y, 0f);
            GL.Vertex3(worldX, origin.y + rows * tileSize, 0f);
        }

        for (int y = 0; y <= rows; y++)
        {
            float worldY = origin.y + y * tileSize;
            GL.Vertex3(origin.x, worldY, 0f);
            GL.Vertex3(origin.x + columns * tileSize, worldY, 0f);
        }

        GL.End();
    }

    // Every tile still waiting for a Builder, so an order that's been placed is visibly pending
    // rather than looking like a click that did nothing.
    private static void DrawPendingJobs(PlayerBase viewedBase)
    {
        float half = viewedBase.TileSize * 0.3f;

        GL.Begin(GL.QUADS);

        for (int y = 0; y < viewedBase.GridRows; y++)
        {
            for (int x = 0; x < viewedBase.GridColumns; x++)
            {
                Vector2Int tile = new Vector2Int(x, y);
                if (!viewedBase.HasPendingJob(tile, out bool fill)) continue;

                GL.Color(fill ? new Color(0.3f, 0.55f, 1f, 0.55f) : new Color(1f, 0.55f, 0.1f, 0.55f));
                DrawQuad(viewedBase.TileCenter(tile), half);
            }
        }

        GL.End();
    }

    private void DrawHoverHighlight(PlayerBase viewedBase)
    {
        if (!TryHoveredTile(viewedBase, out Vector2Int tile)) return;

        bool allowed = IsAllowedOn(viewedBase, tile);

        GL.Begin(GL.QUADS);
        GL.Color(allowed ? new Color(0.2f, 1f, 0.2f, 0.35f) : new Color(1f, 0.2f, 0.2f, 0.35f));
        DrawQuad(viewedBase.TileCenter(tile), viewedBase.TileSize * 0.5f);
        GL.End();
    }

    private static void DrawQuad(Vector3 center, float half)
    {
        GL.Vertex3(center.x - half, center.y - half, 0f);
        GL.Vertex3(center.x + half, center.y - half, 0f);
        GL.Vertex3(center.x + half, center.y + half, 0f);
        GL.Vertex3(center.x - half, center.y + half, 0f);
    }
}
