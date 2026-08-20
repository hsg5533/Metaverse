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
    /// <summary>Shorthand for a literal vector, so a part fits on one line.</summary>
    static Vector3 V(float x, float y, float z) => new(x, y, z);

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
            var rigs = FindObjectsByType<GearPreview>();
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

        Slot entry = instance.Stand(Mathf.Clamp(slot, 0, instance.slots.Length - 1));
        entry.Wanted = true;

        if (entry.Texture != null)
        {
            GUI.DrawTexture(rect, entry.Texture, ScaleMode.ScaleToFit, true);
        }
    }

    void Awake()
    {
        foreach (var other in FindObjectsByType<GearPreview>())
        {
            if (other != this)
            {
                Destroy(other.gameObject);
            }
        }

        rigOrigin = V(Random.Range(-4000f, 4000f), -1000f, Random.Range(-4000f, 4000f));
        transform.position = rigOrigin;
    }

    /// <summary>
    /// The stand for one slot, built the first time something asks to draw it. There is a slot
    /// for every kind of thing the bag can hold, and a camera and a render texture each is a
    /// bill nobody should pay for two dozen items to look at four.
    /// Ten apart, so no camera can see the model belonging to the slot next door.
    /// </summary>
    Slot Stand(int index)
    {
        return slots[index] ??= Setup(V(index * 10f, 0f, 0f), ViewSize(index));
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
            return piece.IsFood ? 0.4f
                : piece.Weapon ? 0.62f
                : 0.5f;
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

        // Before the flags are cleared: this is what decides which stands get a model at all.
        BuildModels();

        foreach (var slot in slots)
        {
            if (slot != null)
            {
                // Asked for again by the next OnGUI or it stops rendering: a bag of two dozen
                // kinds only ever has a handful of rows on screen, and the rest are behind the
                // scroll view filming nothing anybody looks at.
                slot.View.enabled = slot.Wanted;
                slot.Wanted = false;
            }
        }

        // A slow turn plus a fixed tilt shows the depth of every model.
        var spin = Quaternion.Euler(14f, Time.time * 45f, 0f);
        for (int i = 0; i < slots.Length; i++)
        {
            if (slots[i] == null)
            {
                continue;
            }

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
        camera.transform.localPosition = pivot + V(0f, 0f, -4f);
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
            if (slots[i] == null || !slots[i].Wanted)
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

            // Only the material stacks and the campfire's dishes have a model of their own
            // to fall back on; the rest wait for the avatar to exist. An empty gear slot
            // draws nothing: there is no such thing as default gear any more.
            bool buildsItsOwn =
                i == Ore
                || i == Herb
                || i == Wood
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
            Weapon => gear.PartsFor(gear.Weapon.Value),
            Armor => gear.PartsFor(gear.Armor.Value),
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
            BuildFood(root, slot - Piece);
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
        }

        return root.gameObject;
    }

    /// <summary>A distinct shape per piece of food, not just a recolour.</summary>
    static void BuildFood(Transform root, int piece)
    {
        switch (piece)
        {
            case PlayerGear.Potion:
                BuildPotion(root);
                break;
            case PlayerGear.FoodFirst:
                BuildStew(root);
                break;
            case PlayerGear.FoodFirst + 1:
                BuildSoup(root);
                break;
            case PlayerGear.FoodFirst + 2:
                BuildTea(root);
                break;
            case PlayerGear.FoodFirst + 3:
                BuildGrilledFish(root);
                break;
            case PlayerGear.FoodFirst + 4:
                BuildPorridge(root);
                break;
        }
    }

    /// <summary>The shop potion: a round flask of red under a corked neck.</summary>
    static void BuildPotion(Transform root)
    {
        Material glass = Paint(new Color(0.62f, 0.74f, 0.78f));
        Material liquid = Paint(new Color(0.86f, 0.20f, 0.26f));
        Material cork = Paint(new Color(0.58f, 0.42f, 0.24f));

        Part(root, "Belly", V(0f, -0.08f, 0f), V(0.3f, 0.26f, 0.3f), glass);
        Part(root, "BellyCut", V(0f, -0.08f, 0f), V(0.24f, 0.3f, 0.24f), glass, V(0f, 45f, 0f));
        Part(root, "Liquid", V(0f, -0.11f, 0f), V(0.26f, 0.16f, 0.26f), liquid);
        Part(root, "LiquidCut", V(0f, -0.11f, 0f), V(0.21f, 0.18f, 0.21f), liquid, V(0f, 45f, 0f));
        Part(root, "Neck", V(0f, 0.1f, 0f), V(0.11f, 0.14f, 0.11f), glass);
        Part(root, "Cork", V(0f, 0.2f, 0f), V(0.09f, 0.08f, 0.09f), cork);
    }

    /// <summary>The cooked one: a deep bowl of porridge with a spoon standing in it.</summary>
    static void BuildPorridge(Transform root)
    {
        Material bowl = Paint(new Color(0.42f, 0.46f, 0.52f));
        Material rim = Paint(new Color(0.60f, 0.64f, 0.70f));
        Material grain = Paint(new Color(0.90f, 0.84f, 0.64f));
        Material herb = Paint(new Color(0.36f, 0.66f, 0.34f));
        Material spoon = Paint(new Color(0.62f, 0.46f, 0.28f));

        Part(root, "Bowl", V(0f, -0.15f, 0f), V(0.4f, 0.18f, 0.4f), bowl);
        Part(root, "Rim", V(0f, -0.06f, 0f), V(0.44f, 0.04f, 0.44f), rim);
        Part(root, "Porridge", V(0f, -0.03f, 0f), V(0.34f, 0.05f, 0.34f), grain);
        Part(root, "HerbA", V(-0.07f, 0.01f, 0.04f), V(0.09f, 0.03f, 0.05f), herb, V(0f, 24f, 0f));
        Part(root, "HerbB", V(0.06f, 0.01f, -0.05f), V(0.08f, 0.03f, 0.05f), herb, V(0f, -32f, 0f));
        Part(root, "Spoon", V(0.1f, 0.08f, -0.02f), V(0.04f, 0.22f, 0.04f), spoon, V(0f, 0f, 18f));
    }

    /// <summary>A bowl with chunks poking out of the broth.</summary>
    static void BuildStew(Transform root)
    {
        Material bowl = Paint(new Color(0.80f, 0.76f, 0.68f));
        Material rim = Paint(new Color(0.60f, 0.54f, 0.44f));
        Material broth = Paint(new Color(0.74f, 0.32f, 0.20f));
        Material chunk = Paint(new Color(0.86f, 0.62f, 0.30f));

        Part(root, "Bowl", V(0f, -0.16f, 0f), V(0.42f, 0.14f, 0.42f), bowl);
        Part(root, "Rim", V(0f, -0.09f, 0f), V(0.46f, 0.04f, 0.46f), rim);
        Part(root, "Broth", V(0f, -0.05f, 0f), V(0.34f, 0.05f, 0.34f), broth);
        Part(
            root,
            "ChunkA",
            V(-0.08f, -0.01f, 0.05f),
            V(0.08f, 0.08f, 0.08f),
            chunk,
            V(0f, 20f, 0f)
        );
        Part(
            root,
            "ChunkB",
            V(0.07f, -0.01f, -0.04f),
            V(0.07f, 0.07f, 0.07f),
            chunk,
            V(0f, -15f, 0f)
        );
    }

    /// <summary>An iron pot with two side handles and a slick of broth on top.</summary>
    static void BuildSoup(Transform root)
    {
        Material pot = Paint(new Color(0.22f, 0.22f, 0.25f));
        Material trim = Paint(new Color(0.5f, 0.5f, 0.54f));
        Material broth = Paint(new Color(0.30f, 0.46f, 0.62f));

        Part(root, "Pot", V(0f, -0.14f, 0f), V(0.4f, 0.22f, 0.4f), pot);
        Part(root, "Rim", V(0f, -0.03f, 0f), V(0.44f, 0.04f, 0.44f), trim);
        Part(root, "Broth", V(0f, -0.01f, 0f), V(0.32f, 0.03f, 0.32f), broth);
        Part(root, "HandleLeft", V(-0.24f, -0.06f, 0f), V(0.06f, 0.05f, 0.05f), trim);
        Part(root, "HandleRight", V(0.24f, -0.06f, 0f), V(0.06f, 0.05f, 0.05f), trim);
    }

    /// <summary>A cup and saucer with a handle, daintier than the two bowls.</summary>
    static void BuildTea(Transform root)
    {
        Material cup = Paint(new Color(0.90f, 0.88f, 0.82f));
        Material trim = Paint(new Color(0.52f, 0.72f, 0.40f));

        Part(root, "Saucer", V(0f, -0.22f, 0f), V(0.36f, 0.03f, 0.36f), cup);
        Part(root, "Cup", V(0f, -0.1f, 0f), V(0.26f, 0.22f, 0.26f), cup);
        Part(root, "Rim", V(0f, 0.01f, 0f), V(0.28f, 0.03f, 0.28f), trim);
        Part(root, "Tea", V(0f, -0.02f, 0f), V(0.2f, 0.03f, 0.2f), trim);
        Part(root, "Handle", V(0.17f, -0.08f, 0f), V(0.05f, 0.1f, 0.03f), trim, V(0f, 0f, 90f));
    }

    /// <summary>A whole fish on a plate, charred stripes and all.</summary>
    static void BuildGrilledFish(Transform root)
    {
        Material plate = Paint(new Color(0.86f, 0.84f, 0.78f));
        Material fish = Paint(new Color(0.74f, 0.58f, 0.34f));
        Material char_ = Paint(new Color(0.30f, 0.20f, 0.14f));

        Part(root, "Plate", V(0f, -0.18f, 0f), V(0.46f, 0.05f, 0.46f), plate);
        Part(root, "Body", V(0f, -0.1f, 0f), V(0.36f, 0.09f, 0.16f), fish);
        Part(root, "Tail", V(0f, -0.1f, -0.19f), V(0.08f, 0.09f, 0.1f), fish, V(0f, 0f, 20f));
        Part(
            root,
            "CharStripeA",
            V(-0.06f, -0.06f, 0f),
            V(0.03f, 0.02f, 0.16f),
            char_,
            V(0f, 20f, 0f)
        );
        Part(
            root,
            "CharStripeB",
            V(0.06f, -0.06f, 0f),
            V(0.03f, 0.02f, 0.16f),
            char_,
            V(0f, -20f, 0f)
        );
    }

    /// <summary>A lump of grey rock with the blue crystal still growing out of it.</summary>
    static void BuildOre(Transform root)
    {
        Material stone = Paint(new Color(0.45f, 0.46f, 0.50f));
        Material crystal = Paint(new Color(0.42f, 0.82f, 0.92f));

        Part(root, "Rock", V(0f, -0.08f, 0f), V(0.42f, 0.3f, 0.4f), stone, V(0f, 24f, 8f));
        Part(
            root,
            "RockChip",
            V(0.16f, -0.16f, -0.1f),
            V(0.22f, 0.18f, 0.2f),
            stone,
            V(12f, -30f, 0f)
        );

        for (int i = 0; i < 3; i++)
        {
            Part(
                root,
                "Crystal" + i,
                V((i - 1) * 0.14f, 0.1f + (i == 1 ? 0.08f : 0f), (i - 1) * 0.06f),
                V(0.12f, 0.3f + (i == 1 ? 0.14f : 0f), 0.12f),
                crystal,
                V(0f, 45f, (i - 1) * 16f)
            );
        }
    }

    /// <summary>A sprig: stem, four leaves and a flower on top.</summary>
    static void BuildHerb(Transform root)
    {
        Material stem = Paint(new Color(0.30f, 0.58f, 0.30f));
        Material leaf = Paint(new Color(0.40f, 0.78f, 0.42f));
        Material flower = Paint(new Color(0.92f, 0.86f, 0.42f));

        Part(root, "Stem", V(0f, -0.02f, 0f), V(0.05f, 0.5f, 0.05f), stem);

        for (int i = 0; i < 4; i++)
        {
            float side = i % 2 == 0 ? -1f : 1f;
            Part(
                root,
                "Leaf" + i,
                V(side * 0.14f, -0.14f + i * 0.1f, 0f),
                V(0.26f, 0.05f, 0.14f),
                leaf,
                V(0f, i * 24f, side * 22f)
            );
        }

        Part(root, "Bud", V(0f, 0.26f, 0f), V(0.14f, 0.14f, 0.14f), flower, V(0f, 45f, 45f));
    }

    /// <summary>A cut log: bark, pale end grain, and a smaller piece leaning on it.</summary>
    static void BuildWood(Transform root)
    {
        Material bark = Paint(new Color(0.54f, 0.38f, 0.24f));
        Material grain = Paint(new Color(0.80f, 0.66f, 0.44f));

        Part(root, "Log", V(0f, -0.04f, 0f), V(0.24f, 0.5f, 0.24f), bark, V(0f, 0f, 90f));
        Part(root, "GrainLeft", V(-0.25f, -0.04f, 0f), V(0.02f, 0.2f, 0.2f), grain);
        Part(root, "GrainRight", V(0.25f, -0.04f, 0f), V(0.02f, 0.2f, 0.2f), grain);
        Part(
            root,
            "Branch",
            V(0.02f, 0.18f, -0.08f),
            V(0.14f, 0.34f, 0.14f),
            bark,
            V(0f, 20f, 62f)
        );
        Part(
            root,
            "BranchGrain",
            V(0.17f, 0.25f, -0.08f),
            V(0.02f, 0.12f, 0.12f),
            grain,
            V(0f, 20f, 62f)
        );
    }

    static Material Paint(Color color)
    {
        var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
        return new Material(shader) { color = color };
    }

    static void Part(
        Transform parent,
        string name,
        Vector3 position,
        Vector3 scale,
        Material material,
        Vector3 rotation = default
    )
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
