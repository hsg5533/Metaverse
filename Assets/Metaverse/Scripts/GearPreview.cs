using UnityEngine;

/// <summary>
/// Draws what the inventory holds as real 3D models instead of flat icons: the worn gear
/// and every material stack. A small rig sits far below the world with one orthographic
/// camera per slot rendering into a texture, and the models turn slowly so the shape reads
/// as solid. The sword is cloned straight off the avatar's hand, so it always matches the
/// weapon the scene builder made.
/// </summary>
public class GearPreview : MonoBehaviour
{
    public const int Weapon = 0;
    public const int Armor = 1;
    public const int Ore = 2;
    public const int Herb = 3;
    public const int Wood = 4;

    /// <summary>Far below the map, so nothing else lands in a preview camera's frustum.</summary>
    static readonly Vector3 RigOrigin = new Vector3(0f, -1000f, 0f);

    /// <summary>How wide a view each slot needs, and how far its model leans on the tilt.</summary>
    static readonly float[] ViewSizes = { 0.62f, 0.46f, 0.4f, 0.4f, 0.4f };

    static GearPreview instance;

    class Slot
    {
        public Transform Model;
        public Camera View;
        public RenderTexture Texture;
        public Vector3 Pivot;
    }

    readonly Slot[] slots = new Slot[ViewSizes.Length];

    /// <summary>Draws one slot's model into a rect.</summary>
    public static void Draw(Rect rect, int slot)
    {
        // Unity's fake-null: a plain ??= would keep a destroyed rig from a previous session.
        if (instance == null)
        {
            var host = new GameObject("GearPreview") { hideFlags = HideFlags.HideAndDontSave };
            instance = host.AddComponent<GearPreview>();
        }

        var texture = instance.slots[Mathf.Clamp(slot, 0, instance.slots.Length - 1)].Texture;
        if (texture != null)
        {
            GUI.DrawTexture(rect, texture, ScaleMode.ScaleToFit, true);
        }
    }

    void Awake()
    {
        transform.position = RigOrigin;

        // Ten apart, so no camera can see the model belonging to the slot next door.
        for (int i = 0; i < slots.Length; i++)
        {
            slots[i] = Setup(new Vector3(i * 10f, 0f, 0f), ViewSizes[i]);
        }
    }

    void OnDestroy()
    {
        foreach (var slot in slots)
        {
            if (slot?.Texture != null)
            {
                slot.Texture.Release();
                Destroy(slot.Texture);
            }
        }
    }

    void LateUpdate()
    {
        bool visible = PlayerInventory.WindowOpen;
        foreach (var slot in slots)
        {
            slot.View.enabled = visible;
        }

        if (!visible)
        {
            return;
        }

        BuildModels();

        // A slow turn plus a fixed tilt shows the depth of every model.
        var spin = Quaternion.Euler(14f, Time.time * 45f, 0f);
        for (int i = 0; i < slots.Length; i++)
        {
            if (slots[i].Model != null)
            {
                slots[i].Model.localRotation = i == Weapon ? spin * Quaternion.Euler(0f, 0f, 22f) : spin;
            }
        }
    }

    Slot Setup(Vector3 pivot, float size)
    {
        var slot = new Slot
        {
            Pivot = pivot,
            Texture = new RenderTexture(160, 160, 16) { name = "GearPreview", antiAliasing = 2 },
        };

        var camera = new GameObject("PreviewCamera").AddComponent<Camera>();
        camera.transform.SetParent(transform, false);
        camera.transform.localPosition = pivot + new Vector3(0f, 0f, -4f);
        camera.orthographic = true;
        camera.orthographicSize = size;
        camera.nearClipPlane = 0.1f;
        camera.farClipPlane = 8f;
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(0f, 0f, 0f, 0f);
        camera.targetTexture = slot.Texture;
        camera.enabled = false;
        slot.View = camera;
        return slot;
    }

    void BuildModels()
    {
        if (slots[Weapon].Model == null)
        {
            var sword = FindSword();
            if (sword != null)
            {
                slots[Weapon].Model = Mount(Instantiate(sword, transform), slots[Weapon].Pivot);
            }
        }

        for (int i = Armor; i < slots.Length; i++)
        {
            if (slots[i].Model == null)
            {
                slots[i].Model = Mount(BuildModel(i), slots[i].Pivot);
            }
        }
    }

    /// <summary>The sword the avatar is actually holding, so the icon never drifts from the model.</summary>
    static GameObject FindSword()
    {
        var avatar = PlayerAvatar.Local;
        if (avatar == null)
        {
            return null;
        }

        foreach (var child in avatar.GetComponentsInChildren<Transform>(true))
        {
            if (child.name == "Sword")
            {
                return child.gameObject;
            }
        }

        return null;
    }

    /// <summary>
    /// Hangs the model under a mount sitting on its camera's axis, shifted so the model's own
    /// centre is on the mount. Turning the mount then spins the model in place.
    /// </summary>
    Transform Mount(GameObject model, Vector3 pivot)
    {
        var mount = new GameObject("Mount").transform;
        mount.SetParent(transform, false);
        mount.localPosition = pivot;

        // Keeps the world scale the model already has, so the sword shows at its real size.
        model.transform.SetParent(mount, true);
        model.transform.localPosition = Vector3.zero;
        model.transform.localRotation = Quaternion.identity;

        var renderers = model.GetComponentsInChildren<Renderer>();
        if (renderers.Length > 0)
        {
            var bounds = renderers[0].bounds;
            foreach (var renderer in renderers)
            {
                bounds.Encapsulate(renderer.bounds);
            }

            model.transform.position += mount.position - bounds.center;
        }

        return mount;
    }

    GameObject BuildModel(int slot)
    {
        var root = new GameObject("PreviewModel" + slot).transform;
        root.SetParent(transform, false);

        switch (slot)
        {
            case Ore:
                BuildOre(root);
                break;
            case Herb:
                BuildHerb(root);
                break;
            case Wood:
                BuildWood(root);
                break;
            default:
                BuildArmor(root);
                break;
        }

        return root.gameObject;
    }

    /// <summary>A breastplate: chest, collar, belt, pauldrons and a centre ridge.</summary>
    static void BuildArmor(Transform root)
    {
        Material plate = Paint(new Color(0.70f, 0.53f, 0.30f));
        Material trim = Paint(new Color(0.88f, 0.74f, 0.38f));

        Part(root, "Chest", new Vector3(0f, 0.02f, 0f), new Vector3(0.42f, 0.46f, 0.24f), plate);
        Part(root, "Ridge", new Vector3(0f, 0.02f, 0.12f), new Vector3(0.07f, 0.44f, 0.03f), trim);
        Part(root, "Collar", new Vector3(0f, 0.26f, 0f), new Vector3(0.3f, 0.07f, 0.26f), trim);
        Part(root, "Belt", new Vector3(0f, -0.2f, 0f), new Vector3(0.44f, 0.07f, 0.26f), trim);
        Part(root, "PauldronLeft", new Vector3(-0.27f, 0.17f, 0f), new Vector3(0.16f, 0.15f, 0.26f), plate);
        Part(root, "PauldronRight", new Vector3(0.27f, 0.17f, 0f), new Vector3(0.16f, 0.15f, 0.26f), plate);
    }

    /// <summary>A lump of grey rock with the blue crystal still growing out of it.</summary>
    static void BuildOre(Transform root)
    {
        Material stone = Paint(new Color(0.45f, 0.46f, 0.50f));
        Material crystal = Paint(new Color(0.42f, 0.82f, 0.92f));

        Part(root, "Rock", new Vector3(0f, -0.08f, 0f), new Vector3(0.42f, 0.3f, 0.4f), stone, new Vector3(0f, 24f, 8f));
        Part(root, "RockChip", new Vector3(0.16f, -0.16f, -0.1f), new Vector3(0.22f, 0.18f, 0.2f), stone, new Vector3(12f, -30f, 0f));

        for (int i = 0; i < 3; i++)
        {
            Part(root, "Crystal" + i,
                new Vector3((i - 1) * 0.14f, 0.1f + (i == 1 ? 0.08f : 0f), (i - 1) * 0.06f),
                new Vector3(0.12f, 0.3f + (i == 1 ? 0.14f : 0f), 0.12f), crystal,
                new Vector3(0f, 45f, (i - 1) * 16f));
        }
    }

    /// <summary>A sprig: stem, four leaves and a flower on top.</summary>
    static void BuildHerb(Transform root)
    {
        Material stem = Paint(new Color(0.30f, 0.58f, 0.30f));
        Material leaf = Paint(new Color(0.40f, 0.78f, 0.42f));
        Material flower = Paint(new Color(0.92f, 0.86f, 0.42f));

        Part(root, "Stem", new Vector3(0f, -0.02f, 0f), new Vector3(0.05f, 0.5f, 0.05f), stem);

        for (int i = 0; i < 4; i++)
        {
            float side = i % 2 == 0 ? -1f : 1f;
            Part(root, "Leaf" + i,
                new Vector3(side * 0.14f, -0.14f + i * 0.1f, 0f),
                new Vector3(0.26f, 0.05f, 0.14f), leaf,
                new Vector3(0f, i * 24f, side * 22f));
        }

        Part(root, "Bud", new Vector3(0f, 0.26f, 0f), new Vector3(0.14f, 0.14f, 0.14f), flower, new Vector3(0f, 45f, 45f));
    }

    /// <summary>A cut log: bark, pale end grain, and a smaller piece leaning on it.</summary>
    static void BuildWood(Transform root)
    {
        Material bark = Paint(new Color(0.54f, 0.38f, 0.24f));
        Material grain = Paint(new Color(0.80f, 0.66f, 0.44f));

        Part(root, "Log", new Vector3(0f, -0.04f, 0f), new Vector3(0.24f, 0.5f, 0.24f), bark, new Vector3(0f, 0f, 90f));
        Part(root, "GrainLeft", new Vector3(-0.25f, -0.04f, 0f), new Vector3(0.02f, 0.2f, 0.2f), grain);
        Part(root, "GrainRight", new Vector3(0.25f, -0.04f, 0f), new Vector3(0.02f, 0.2f, 0.2f), grain);
        Part(root, "Branch", new Vector3(0.02f, 0.18f, -0.08f), new Vector3(0.14f, 0.34f, 0.14f), bark, new Vector3(0f, 20f, 62f));
        Part(root, "BranchGrain", new Vector3(0.17f, 0.25f, -0.08f), new Vector3(0.02f, 0.12f, 0.12f), grain, new Vector3(0f, 20f, 62f));
    }

    static Material Paint(Color color)
    {
        var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
        return new Material(shader) { color = color };
    }

    static void Part(Transform parent, string name, Vector3 position, Vector3 scale, Material material)
    {
        Part(parent, name, position, scale, material, Vector3.zero);
    }

    static void Part(Transform parent, string name, Vector3 position, Vector3 scale, Material material, Vector3 rotation)
    {
        var part = GameObject.CreatePrimitive(PrimitiveType.Cube);
        part.name = name;
        Destroy(part.GetComponent<Collider>());
        part.transform.SetParent(parent, false);
        part.transform.localPosition = position;
        part.transform.localRotation = Quaternion.Euler(rotation);
        part.transform.localScale = scale;
        part.GetComponent<Renderer>().sharedMaterial = material;
    }
}
