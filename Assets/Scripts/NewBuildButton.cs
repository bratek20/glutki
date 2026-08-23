using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.UI;

// Lets the player enter "build mode" for their selected base: draws its interior tile grid on
// the ground plus a hover highlight (green if the hovered tile is free to build on - i.e. a plain
// floor tile - red otherwise), and turns it into a ResourceStock tile on click. Clicking the
// button again, right-clicking, or pressing Escape all cancel without building.
public class NewBuildButton : MonoBehaviour
{
    [SerializeField] private Button button;
    [SerializeField] private TMP_Text label;

    private static Material gridMaterial;

    private bool buildModeActive;

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
    }

    private void Update()
    {
        PlayerBase viewedBase = ViewManager.CurrentView == ViewMode.Base ? ViewManager.ViewedBase : null;
        bool canBuildHere = viewedBase != null && viewedBase.IsOwnedByLocalPlayer && viewedBase.CanBuildStock;

        if (buildModeActive && !canBuildHere) buildModeActive = false;

        button.interactable = canBuildHere;
        label.text = buildModeActive ? "Cancel Build" : "New Build";

        if (buildModeActive) HandleBuildModeInput(viewedBase);
    }

    private void HandleBuildModeInput(PlayerBase viewedBase)
    {
        if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
        {
            buildModeActive = false;
            return;
        }

        Mouse mouse = Mouse.current;
        if (mouse == null || Camera.main == null) return;

        if (mouse.rightButton.wasPressedThisFrame)
        {
            buildModeActive = false;
            return;
        }

        if (!mouse.leftButton.wasPressedThisFrame) return;
        if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()) return;

        Vector2 worldPoint = Camera.main.ScreenToWorldPoint(mouse.position.ReadValue());
        Vector2Int tile = viewedBase.WorldToTile(worldPoint);
        if (!viewedBase.IsTileBuildable(tile)) return;

        viewedBase.CmdBuildTile(tile, TileType.ResourceStock);
        buildModeActive = false;
    }

    private void OnClicked()
    {
        PlayerBase viewedBase = ViewManager.CurrentView == ViewMode.Base ? ViewManager.ViewedBase : null;
        if (viewedBase == null || !viewedBase.IsOwnedByLocalPlayer || !viewedBase.CanBuildStock) return;

        buildModeActive = !buildModeActive;
    }

    private void OnEndCameraRendering(ScriptableRenderContext context, Camera camera)
    {
        if (!buildModeActive || camera != Camera.main || ViewManager.CurrentView != ViewMode.Base) return;

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

    private static void DrawHoverHighlight(PlayerBase viewedBase)
    {
        if (Mouse.current == null || Camera.main == null) return;

        Vector2 worldPoint = Camera.main.ScreenToWorldPoint(Mouse.current.position.ReadValue());
        Vector2Int tile = viewedBase.WorldToTile(worldPoint);
        bool buildable = viewedBase.IsTileBuildable(tile);

        Vector3 center = viewedBase.TileCenter(tile);
        float half = viewedBase.TileSize * 0.5f;
        Color fillColor = buildable ? new Color(0.2f, 1f, 0.2f, 0.35f) : new Color(1f, 0.2f, 0.2f, 0.35f);

        GL.Begin(GL.QUADS);
        GL.Color(fillColor);
        GL.Vertex3(center.x - half, center.y - half, 0f);
        GL.Vertex3(center.x + half, center.y - half, 0f);
        GL.Vertex3(center.x + half, center.y + half, 0f);
        GL.Vertex3(center.x - half, center.y + half, 0f);
        GL.End();
    }
}
