using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

// Editor-only tooling for adding new UI to GameScene. Per project convention (see CLAUDE.md's "UI
// element creation" section), Claude never hand-edits scene files to add UI - instead it writes a
// menu action here that the user runs themselves inside the Editor, which builds and fully wires
// the GameObject hierarchy in one click.
public static class ClaudeUiTools
{
    private const string ButtonSpritePath = "UI/Skin/UISprite.psd";
    private const string PanelSpritePath = "UI/Skin/Background.psd";

    [MenuItem("Claude/Create Attacker Spawn Button")]
    public static void CreateAttackerSpawnButton()
    {
        Transform canvas = FindCanvas();
        if (canvas == null) return;

        Transform existing = canvas.Find("SpawnAttacker_Button");
        if (existing != null)
        {
            EditorUtility.DisplayDialog("Claude", "SpawnAttacker_Button already exists in this scene.", "OK");
            Selection.activeGameObject = existing.gameObject;
            return;
        }

        // Same anchor/pivot convention as the existing SpawnUnit_Button (anchored bottom-left,
        // pivot at the rect's own center) - sits directly above it with a 15px gap.
        GameObject root = CreateButton("SpawnAttacker_Button", canvas, "Spawn Attacker",
            anchorMin: new Vector2(0f, 0f), anchorMax: new Vector2(0f, 0f), pivot: new Vector2(0.5f, 0.5f),
            anchoredPosition: new Vector2(200f, 140f), sizeDelta: new Vector2(300f, 50f),
            out Button button, out TMP_Text label);

        SpawnAttackerButton behaviour = Undo.AddComponent<SpawnAttackerButton>(root);
        SerializedObject so = new SerializedObject(behaviour);
        so.FindProperty("button").objectReferenceValue = button;
        so.FindProperty("label").objectReferenceValue = label;
        so.ApplyModifiedPropertiesWithoutUndo();

        FinishCreation(root, "Create Attacker Spawn Button");
    }

    [MenuItem("Claude/Create New Build Button")]
    public static void CreateNewBuildButton()
    {
        Transform canvas = FindCanvas();
        if (canvas == null) return;

        Transform existing = canvas.Find("NewBuild_Button");
        if (existing != null)
        {
            EditorUtility.DisplayDialog("Claude", "NewBuild_Button already exists in this scene.", "OK");
            Selection.activeGameObject = existing.gameObject;
            return;
        }

        // Same anchor/pivot convention as SpawnUnit_Button/SpawnAttacker_Button - stacks above them.
        GameObject root = CreateButton("NewBuild_Button", canvas, "New Build",
            anchorMin: new Vector2(0f, 0f), anchorMax: new Vector2(0f, 0f), pivot: new Vector2(0.5f, 0.5f),
            anchoredPosition: new Vector2(200f, 200f), sizeDelta: new Vector2(300f, 50f),
            out Button button, out TMP_Text label);

        NewBuildButton behaviour = Undo.AddComponent<NewBuildButton>(root);
        SerializedObject so = new SerializedObject(behaviour);
        so.FindProperty("button").objectReferenceValue = button;
        so.FindProperty("label").objectReferenceValue = label;
        so.ApplyModifiedPropertiesWithoutUndo();

        FinishCreation(root, "Create New Build Button");
    }

    [MenuItem("Claude/Create Attack Order Popup")]
    public static void CreateAttackOrderPopup()
    {
        Transform canvas = FindCanvas();
        if (canvas == null) return;

        Transform existing = canvas.Find("AttackOrder_Popup");
        if (existing != null)
        {
            EditorUtility.DisplayDialog("Claude", "AttackOrder_Popup already exists in this scene.", "OK");
            Selection.activeGameObject = existing.gameObject;
            return;
        }

        // Full-screen blocker so clicks outside the popup don't reach the world underneath it.
        GameObject blocker = CreateUiObject("AttackOrder_Blocker", canvas);
        RectTransform blockerRect = blocker.GetComponent<RectTransform>();
        SetRect(blockerRect, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
        Image blockerImage = blocker.AddComponent<Image>();
        blockerImage.color = new Color(0f, 0f, 0f, 0.6f);
        blockerImage.raycastTarget = true;

        GameObject popup = CreateUiObject("AttackOrder_Popup", canvas);
        RectTransform popupRect = popup.GetComponent<RectTransform>();
        SetRect(popupRect, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(520f, 320f));
        Image popupImage = popup.AddComponent<Image>();
        popupImage.sprite = LoadSprite(PanelSpritePath);
        popupImage.type = Image.Type.Sliced;
        popupImage.color = new Color(0.1f, 0.1f, 0.1f, 0.95f);

        TMP_Text titleText = CreateTmpText("Title (TMP)", popup.transform, "Attack Bot Base",
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -25f), new Vector2(480f, 40f), 28f, FontStyles.Bold);

        TMP_Text hpText = CreateTmpText("HP (TMP)", popup.transform, "HP: 0 / 0",
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -70f), new Vector2(400f, 30f), 20f, FontStyles.Normal);

        CreateSlider("Attacker_Slider", popup.transform,
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -130f), new Vector2(440f, 20f), out Slider slider);

        TMP_Text countLabel = CreateTmpText("Count (TMP)", popup.transform, "Send 0 / 0 attackers",
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -165f), new Vector2(400f, 30f), 20f, FontStyles.Normal);

        CreateButton("Attack_Button", popup.transform, "Attack",
            new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(-110f, 25f), new Vector2(200f, 50f),
            out Button attackButton, out TMP_Text _);

        CreateButton("Cancel_Button", popup.transform, "Cancel",
            new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(110f, 25f), new Vector2(200f, 50f),
            out Button cancelButton, out TMP_Text _);

        AttackOrderPopup behaviour = Undo.AddComponent<AttackOrderPopup>(popup);
        SerializedObject so = new SerializedObject(behaviour);
        so.FindProperty("panel").objectReferenceValue = popup;
        so.FindProperty("titleText").objectReferenceValue = titleText;
        so.FindProperty("hpText").objectReferenceValue = hpText;
        so.FindProperty("slider").objectReferenceValue = slider;
        so.FindProperty("countLabel").objectReferenceValue = countLabel;
        so.FindProperty("attackButton").objectReferenceValue = attackButton;
        so.FindProperty("cancelButton").objectReferenceValue = cancelButton;
        so.ApplyModifiedPropertiesWithoutUndo();

        // Both stay active - AttackOrderPopup.Awake() needs to run at scene load to register
        // itself, so visibility is driven by a CanvasGroup (added in Awake) instead of
        // SetActive. Deactivating the GameObject here would silently break that registration.

        Undo.RegisterCreatedObjectUndo(blocker, "Create Attack Order Popup");
        FinishCreation(popup, "Create Attack Order Popup");
    }

    [MenuItem("Claude/Create Game Result Popup")]
    public static void CreateGameResultPopup()
    {
        Transform canvas = FindCanvas();
        if (canvas == null) return;

        Transform existing = canvas.Find("GameResult_Popup");
        if (existing != null)
        {
            EditorUtility.DisplayDialog("Claude", "GameResult_Popup already exists in this scene.", "OK");
            Selection.activeGameObject = existing.gameObject;
            return;
        }

        // Full-screen blocker so clicks outside the popup don't reach the world underneath it.
        GameObject blocker = CreateUiObject("GameResult_Blocker", canvas);
        RectTransform blockerRect = blocker.GetComponent<RectTransform>();
        SetRect(blockerRect, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
        Image blockerImage = blocker.AddComponent<Image>();
        blockerImage.color = new Color(0f, 0f, 0f, 0.6f);
        blockerImage.raycastTarget = true;

        GameObject popup = CreateUiObject("GameResult_Popup", canvas);
        RectTransform popupRect = popup.GetComponent<RectTransform>();
        SetRect(popupRect, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(520f, 240f));
        Image popupImage = popup.AddComponent<Image>();
        popupImage.sprite = LoadSprite(PanelSpritePath);
        popupImage.type = Image.Type.Sliced;
        popupImage.color = new Color(0.1f, 0.1f, 0.1f, 0.95f);

        TMP_Text titleText = CreateTmpText("Title (TMP)", popup.transform, "Game Over",
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -60f), new Vector2(480f, 90f), 28f, FontStyles.Bold);

        CreateButton("Confirm_Button", popup.transform, "Confirm",
            new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 30f), new Vector2(220f, 50f),
            out Button confirmButton, out TMP_Text _);

        GameResultPopup behaviour = Undo.AddComponent<GameResultPopup>(popup);
        SerializedObject so = new SerializedObject(behaviour);
        so.FindProperty("panel").objectReferenceValue = popup;
        so.FindProperty("titleText").objectReferenceValue = titleText;
        so.FindProperty("confirmButton").objectReferenceValue = confirmButton;
        so.ApplyModifiedPropertiesWithoutUndo();

        // Both stay active - GameResultPopup.Awake() needs to run at scene load to register
        // itself, so visibility is driven by a CanvasGroup (added in Awake) instead of SetActive.

        Undo.RegisterCreatedObjectUndo(blocker, "Create Game Result Popup");
        FinishCreation(popup, "Create Game Result Popup");
    }

    private static Transform FindCanvas()
    {
        GameObject canvasObject = GameObject.Find("UI Canvas");
        Canvas canvas = canvasObject != null ? canvasObject.GetComponent<Canvas>() : null;
        if (canvas == null) canvas = Object.FindFirstObjectByType<Canvas>();

        if (canvas == null)
        {
            EditorUtility.DisplayDialog("Claude", "No Canvas found in the open scene. Open GameScene first.", "OK");
            return null;
        }

        return canvas.transform;
    }

    private static void FinishCreation(GameObject root, string undoName)
    {
        Undo.RegisterCreatedObjectUndo(root, undoName);
        Selection.activeGameObject = root;
        EditorSceneManager.MarkSceneDirty(root.scene);
    }

    private static GameObject CreateUiObject(string name, Transform parent)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return go;
    }

    private static void SetRect(RectTransform rect, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 anchoredPosition, Vector2 sizeDelta)
    {
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.pivot = pivot;
        rect.anchoredPosition = anchoredPosition;
        rect.sizeDelta = sizeDelta;
    }

    private static Sprite LoadSprite(string builtinPath)
    {
        return AssetDatabase.GetBuiltinExtraResource<Sprite>(builtinPath);
    }

    // Unity's default UI text color - the stock button/panel sprites are light, so white text
    // would be invisible against them.
    private static readonly Color DefaultTextColor = new Color32(0x38, 0x38, 0x38, 0xFF);

    private static TMP_Text CreateTmpText(string name, Transform parent, string text,
        Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 anchoredPosition, Vector2 sizeDelta,
        float fontSize, FontStyles fontStyle)
    {
        GameObject go = CreateUiObject(name, parent);
        SetRect(go.GetComponent<RectTransform>(), anchorMin, anchorMax, pivot, anchoredPosition, sizeDelta);

        TextMeshProUGUI tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.text = text;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.fontSize = fontSize;
        tmp.fontStyle = fontStyle;
        tmp.color = DefaultTextColor;
        tmp.enableAutoSizing = true;
        tmp.fontSizeMin = 12f;
        tmp.fontSizeMax = fontSize;

        return tmp;
    }

    // Builds a Button (Image + TMP label child) at the given rect. Matches the visual recipe every
    // other button in the scene already uses (UISprite background, sliced, stock color tint).
    private static GameObject CreateButton(string name, Transform parent, string labelText,
        Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 anchoredPosition, Vector2 sizeDelta,
        out Button button, out TMP_Text label)
    {
        GameObject go = CreateUiObject(name, parent);
        SetRect(go.GetComponent<RectTransform>(), anchorMin, anchorMax, pivot, anchoredPosition, sizeDelta);

        Image image = go.AddComponent<Image>();
        image.sprite = LoadSprite(ButtonSpritePath);
        image.type = Image.Type.Sliced;

        button = go.AddComponent<Button>();
        button.targetGraphic = image;

        label = CreateTmpText("Text (TMP)", go.transform, labelText,
            Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero, 24f, FontStyles.Normal);

        return go;
    }

    // Builds a horizontal Slider matching Unity's stock Background/Fill Area+Fill/Handle Slide
    // Area+Handle hierarchy, whole-number values starting at 0.
    private static GameObject CreateSlider(string name, Transform parent,
        Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 anchoredPosition, Vector2 sizeDelta,
        out Slider slider)
    {
        GameObject root = CreateUiObject(name, parent);
        SetRect(root.GetComponent<RectTransform>(), anchorMin, anchorMax, pivot, anchoredPosition, sizeDelta);
        slider = root.AddComponent<Slider>();

        GameObject background = CreateUiObject("Background", root.transform);
        SetRect(background.GetComponent<RectTransform>(), new Vector2(0f, 0.25f), new Vector2(1f, 0.75f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
        Image backgroundImage = background.AddComponent<Image>();
        backgroundImage.sprite = LoadSprite(PanelSpritePath);
        backgroundImage.type = Image.Type.Sliced;
        backgroundImage.color = new Color(0.15f, 0.15f, 0.15f, 1f);

        GameObject fillArea = CreateUiObject("Fill Area", root.transform);
        RectTransform fillAreaRect = fillArea.GetComponent<RectTransform>();
        SetRect(fillAreaRect, new Vector2(0f, 0.25f), new Vector2(1f, 0.75f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
        fillAreaRect.offsetMin = new Vector2(5f, fillAreaRect.offsetMin.y);
        fillAreaRect.offsetMax = new Vector2(-15f, fillAreaRect.offsetMax.y);

        GameObject fill = CreateUiObject("Fill", fillArea.transform);
        RectTransform fillRect = fill.GetComponent<RectTransform>();
        SetRect(fillRect, Vector2.zero, new Vector2(1f, 1f), new Vector2(0f, 0.5f), Vector2.zero, Vector2.zero);
        Image fillImage = fill.AddComponent<Image>();
        fillImage.sprite = LoadSprite(ButtonSpritePath);
        fillImage.type = Image.Type.Sliced;
        fillImage.color = new Color(0.85f, 0.7f, 0.2f, 1f);

        GameObject handleArea = CreateUiObject("Handle Slide Area", root.transform);
        RectTransform handleAreaRect = handleArea.GetComponent<RectTransform>();
        SetRect(handleAreaRect, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
        handleAreaRect.offsetMin = new Vector2(10f, handleAreaRect.offsetMin.y);
        handleAreaRect.offsetMax = new Vector2(-10f, handleAreaRect.offsetMax.y);

        GameObject handle = CreateUiObject("Handle", handleArea.transform);
        RectTransform handleRect = handle.GetComponent<RectTransform>();
        SetRect(handleRect, new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(20f, 0f));
        Image handleImage = handle.AddComponent<Image>();
        handleImage.sprite = LoadSprite(ButtonSpritePath);
        handleImage.type = Image.Type.Sliced;
        handleImage.color = Color.white;

        slider.direction = Slider.Direction.LeftToRight;
        slider.fillRect = fillRect;
        slider.handleRect = handleRect;
        slider.targetGraphic = handleImage;
        slider.transition = Selectable.Transition.ColorTint;
        slider.wholeNumbers = true;
        slider.minValue = 0f;
        slider.maxValue = 1f;
        slider.value = 0f;

        return root;
    }
}
