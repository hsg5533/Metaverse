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

    /// <summary>One slot per carried piece of gear, after the five fixed ones.</summary>
    public const int Piece = 5;

    /// <summary>
    /// Far below the map, so nothing else lands in a preview camera's frustum. The rig picks
    /// its own spot down there: a rig that outlived a reload cannot be found or destroyed
    /// (FindObjectsByType skips HideAndDontSave), so the only defence is not to stand where
    /// it stands.
    /// </summary>
    Vector3 rigOrigin;

    static GearPreview instance;

    class Slot
    {
        /// <summary>Fixed stand in front of the camera; whatever is shown hangs under it.</summary>
        public Transform Mount;

        public Camera View;
        public RenderTexture Texture;
        public Vector3 Pivot;

        /// <summary>The avatar model this slot was cloned from, so a swap is noticed.</summary>
        public GameObject Source;

        /// <summary>Set the moment something asks to draw it; nothing else is built or rendered.</summary>
        public bool Wanted;
    }

    readonly Slot[] slots = new Slot[Piece + PlayerGear.Pieces.Length];

    /// <summary>Draws one slot's model into a rect.</summary>
    public static void Draw(Rect rect, int slot)
    {
        // Unity's fake-null: a plain ??= would keep a destroyed rig from a previous session.
        if (instance == null)
        {
            // A recompile resets the static but leaves the rig itself alive. Keep the first
            // one and bin the rest: two rigs sit at the same spot and every camera films both,
            // which reads as the old gear refusing to go away.
            var rigs = FindObjectsByType<GearPreview>(FindObjectsSortMode.None);
            for (int i = 1; i < rigs.Length; i++)
            {
                Destroy(rigs[i].gameObject);
            }

            instance = rigs.Length > 0 ? rigs[0] : null;
        }

        if (instance == null)
        {
            var host = new GameObject("GearPreview") { hideFlags = HideFlags.HideInHierarchy };
            instance = host.AddComponent<GearPreview>();
        }

        Slot entry = instance.slots[Mathf.Clamp(slot, 0, instance.slots.Length - 1)];
        entry.Wanted = true;

        if (entry.Texture != null)
        {
            GUI.DrawTexture(rect, entry.Texture, ScaleMode.ScaleToFit, true);
        }
    }

    void Awake()
    {
        foreach (var other in FindObjectsByType<GearPreview>(FindObjectsSortMode.None))
        {
            if (other != this)
            {
                Destroy(other.gameObject);
            }
        }

        rigOrigin = new Vector3(Random.Range(-4000f, 4000f), -1000f, Random.Range(-4000f, 4000f));
        transform.position = rigOrigin;

        // Ten apart, so no camera can see the model belonging to the slot next door.
        for (int i = 0; i < slots.Length; i++)
        {
            slots[i] = Setup(new Vector3(i * 10f, 0f, 0f), ViewSize(i));
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

    static float ViewSize(int slot)
    {
        if (slot >= Piece)
        {
            PlayerGear.Piece piece = PlayerGear.Pieces[slot - Piece];
            return piece.IsFood ? 0.4f : piece.Weapon ? 0.62f : 0.5f;
        }

        return slot switch
        {
            Weapon => 0.62f,
            Armor => 0.5f,
            _ => 0.4f, // Ore, Herb, Wood
        };
    }

    void LateUpdate()
    {
        // ponytail: the rig lives only while something that shows a 3D icon is open - the bag
        // or the shop's buy list. Rebuilding a dozen cubes on every open is cheaper than
        // reasoning about what survives a domain reload, and it makes leftovers from an
        // earlier session impossible.
        if (!PlayerInventory.WindowOpen && !ShopNpc.PanelOpen)
        {
            if (instance == this)
            {
                instance = null;
            }

            Destroy(gameObject);
            return;
        }

        foreach (var slot in slots)
        {
            slot.View.enabled = slot.Wanted;
        }

        BuildModels();

        // A slow turn plus a fixed tilt shows the depth of every model.
        var spin = Quaternion.Euler(14f, Time.time * 45f, 0f);
        for (int i = 0; i < slots.Length; i++)
        {
            bool blade = i == Weapon || (i >= Piece && PlayerGear.Pieces[i - Piece].Weapon);
            slots[i].Mount.localRotation = blade ? spin * Quaternion.Euler(0f, 0f, 22f) : spin;
        }
    }

    Slot Setup(Vector3 pivot, float size)
    {
        var mount = new GameObject("Mount").transform;
        mount.SetParent(transform, false);
        mount.localPosition = pivot;

        var slot = new Slot
        {
            Pivot = pivot,
            Mount = mount,
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
        for (int i = 0; i < slots.Length; i++)
        {
            if (!slots[i].Wanted)
            {
                continue;
            }

            GameObject[] parts = WornParts(i);
            GameObject worn = parts.Length > 0 ? parts[0] : null;

            // Gear can be swapped at any moment: if the body is wearing something else than
            // the copy was taken from, the stand is emptied and a fresh copy hung on it.
            if (slots[i].Mount.childCount > 0 && slots[i].Source == worn)
            {
                continue;
            }

            Clear(slots[i].Mount);

            // Only the armour, the material stacks and the campfire's dishes have a model of
            // their own to fall back on; the rest wait for the avatar to exist.
            bool buildsItsOwn = i == Armor || i == Ore || i == Herb || i == Wood
                || (i >= Piece && PlayerGear.Pieces[i - Piece].IsFood);
            if (worn == null && !buildsItsOwn)
            {
                continue;
            }

            GameObject model = worn != null ? Assemble(parts) : BuildModel(i);
            if (model == null)
            {
                continue;
            }

            model.SetActive(true);
            Hang(model, slots[i].Mount);
            slots[i].Source = worn;
        }
    }

    /// <summary>
    /// The models on the avatar this slot should copy, so an icon never drifts from what the
    /// character is actually wearing. Empty means the slot builds its own little model.
    /// </summary>
    static GameObject[] WornParts(int slot)
    {
        var avatar = PlayerAvatar.Local;
        var gear = avatar != null ? avatar.GetComponent<PlayerGear>() : null;
        if (gear == null)
        {
            return System.Array.Empty<GameObject>();
        }

        return slot switch
        {
            Weapon => gear.EquippedParts(true),
            Armor => gear.EquippedParts(false),
            Ore or Herb or Wood => System.Array.Empty<GameObject>(),
            _ => gear.PartsFor(slot - Piece),
        };
    }

    /// <summary>
    /// Copies every part of a piece into one group, keeping how they sit relative to each
    /// other, so a suit of armour shows its shoulders and not just the breastplate.
    /// </summary>
    GameObject Assemble(GameObject[] parts)
    {
        var group = new GameObject("Preview").transform;
        group.SetParent(transform, false);

        var avatar = PlayerAvatar.Local;
        var limbs = avatar != null ? avatar.GetComponent<AvatarLimbAnimator>() : null;

        foreach (var part in parts)
        {
            if (part == null)
            {
                continue;
            }

            // A piece sitting in the bag is switched off on the body; the copy has to show.
            // Copying local transforms (rather than world ones) and adding back only the
            // limb's own fixed pivot offset means the icon always shows the built rest pose -
            // arms hanging straight - never whatever angle the walk cycle or an attack swing
            // has the real arm at right now.
            var clone = Instantiate(part, group, false);
            clone.SetActive(true);

            if (limbs != null && part.transform.parent == limbs.RightArm)
            {
                clone.transform.localPosition += limbs.RightArm.localPosition;
            }
            else if (limbs != null && part.transform.parent == limbs.LeftArm)
            {
                clone.transform.localPosition += limbs.LeftArm.localPosition;
            }
        }

        return group.gameObject;
    }

    /// <summary>
    /// Empties a stand for good: switched off first, because Destroy only takes effect at the
    /// end of the frame and until then it would render on top of the replacement.
    /// </summary>
    static void Clear(Transform mount)
    {
        foreach (Transform child in mount)
        {
            child.gameObject.SetActive(false);
            Destroy(child.gameObject);
        }
    }

    /// <summary>
    /// Hangs the model on its stand, shifted so the model's own centre is on it. Turning the
    /// stand then spins the model in place.
    /// </summary>
    static void Hang(GameObject model, Transform mount)
    {
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
    }

    GameObject BuildModel(int slot)
    {
        var root = new GameObject("PreviewModel" + slot).transform;
        root.SetParent(transform, false);

        if (slot >= Piece && PlayerGear.Pieces[slot - Piece].IsFood)
        {
            BuildFood(root, PlayerGear.Pieces[slot - Piece].Buff);
            return root.gameObject;
        }

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

    /// <summary>A bowl of something hot: base, filling and a wisp of steam. Colour follows the buff.</summary>
    static void BuildFood(Transform root, int buffKind)
    {
        Color fillColor = buffKind switch
        {
            PlayerBuffs.Attack => new Color(0.74f, 0.32f, 0.20f),
            PlayerBuffs.Defense => new Color(0.30f, 0.46f, 0.62f),
            _ => new Color(0.52f, 0.72f, 0.40f),
        };

        Material bowl = Paint(new Color(0.82f, 0.78f, 0.70f));
        Material fill = Paint(fillColor);
        Material steam = Paint(new Color(0.88f, 0.88f, 0.88f));

        Part(root, "Bowl", new Vector3(0f, -0.14f, 0f), new Vector3(0.4f, 0.16f, 0.4f), bowl);
        Part(root, "Filling", new Vector3(0f, -0.04f, 0f), new Vector3(0.32f, 0.06f, 0.32f), fill);
        Part(root, "SteamLeft", new Vector3(-0.08f, 0.2f, 0f), new Vector3(0.05f, 0.22f, 0.05f), steam, new Vector3(0f, 0f, -12f));
        Part(root, "SteamRight", new Vector3(0.08f, 0.22f, 0f), new Vector3(0.05f, 0.26f, 0.05f), steam, new Vector3(0f, 0f, 10f));
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
