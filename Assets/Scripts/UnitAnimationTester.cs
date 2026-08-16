using System.Collections.Generic;
using UnityEngine;
using Mirror;

// Debug tool for the AnimationsTesting scene: drives a unit's Animator/SpriteRenderer directly from
// Inspector fields so every animation state can be posed and compared side-by-side, without
// UnitController/NetworkTransform fighting over the same Animator every frame. Disabled by default
// (see Reset) - enable per-instance on whichever prefabs are dropped into that scene.
public class UnitAnimationTester : MonoBehaviour
{
    [System.Serializable]
    public struct BoolParam
    {
        public string parameterName;
        public bool value;
    }

    [SerializeField] private Animator animator;
    [SerializeField] private SpriteRenderer spriteRenderer;

    [Tooltip("Animator bool parameters to drive, e.g. IsWalking / IsAttacking. Different unit types expose different parameters (check the unit's own Animator Controller) - add/remove entries per instance as needed.")]
    [SerializeField]
    private BoolParam[] boolParams =
    {
        new BoolParam { parameterName = "IsWalking" },
        new BoolParam { parameterName = "IsAttacking" },
    };

    [Header("Facing / tint (mirrors what UnitController normally drives)")]
    [SerializeField] private bool facingLeft;
    [SerializeField] private bool tinted;
    [SerializeField] private Color tintColor = new Color(1f, 0.85f, 0.35f);

    private readonly List<Behaviour> disabledByTester = new List<Behaviour>();

    private void Reset()
    {
        enabled = false;
    }

    // Unit prefabs are normally spawned inactive and only activated by the networking layer once
    // NetworkServer.Spawn runs - which never happens in a plain test scene. A component's OnEnable
    // never fires on an inactive GameObject, so nothing here would run without this: at Play, find
    // every tester (enabled via the checkbox in the Inspector) that's sitting on an inactive
    // GameObject and activate it, so OnEnable then fires normally and disables the conflicting
    // scripts below.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void ActivateEnabledInstances()
    {
        UnitAnimationTester[] testers = FindObjectsByType<UnitAnimationTester>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        foreach (UnitAnimationTester tester in testers)
        {
            if (tester.enabled && !tester.gameObject.activeSelf)
            {
                tester.gameObject.SetActive(true);
            }
        }
    }

    private void OnEnable()
    {
        if (animator == null) animator = GetComponent<Animator>();
        if (spriteRenderer == null) spriteRenderer = GetComponent<SpriteRenderer>();

        DisableConflictingScripts();
    }

    private void OnDisable()
    {
        foreach (Behaviour behaviour in disabledByTester)
        {
            if (behaviour != null) behaviour.enabled = true;
        }
        disabledByTester.Clear();
    }

    // Turns off anything that would otherwise drive this unit's Animator/SpriteRenderer/position
    // every frame and stomp on the values set here - the unit's own AI/movement and any network
    // position sync in particular.
    private void DisableConflictingScripts()
    {
        foreach (Behaviour behaviour in GetComponents<Behaviour>())
        {
            if (behaviour == this || !behaviour.enabled) continue;

            if (behaviour is UnitController || behaviour is NetworkTransformBase || behaviour is NetworkAnimator)
            {
                behaviour.enabled = false;
                disabledByTester.Add(behaviour);
            }
        }
    }

    private void Update()
    {
        if (animator != null)
        {
            foreach (BoolParam param in boolParams)
            {
                if (string.IsNullOrEmpty(param.parameterName)) continue;
                animator.SetBool(param.parameterName, param.value);
            }
        }

        if (spriteRenderer != null)
        {
            spriteRenderer.flipX = facingLeft;
            spriteRenderer.color = tinted ? tintColor : Color.white;
        }
    }
}
