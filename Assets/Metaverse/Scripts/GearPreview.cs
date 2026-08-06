using UnityEngine;

/// <summary>
/// Draws the worn gear into the inventory as real 3D models instead of flat icons.
/// A small rig sits far below the world with one orthographic camera per slot rendering
/// into a texture; the models turn slowly so the shape reads as solid.
/// The sword is cloned straight off the avatar's hand, so it always matches the weapon
/// the scene builder made.
/// </summary>
public class GearPreview : MonoBehaviour
{
    /// <summary>Far below the map, so nothing else lands in a preview camera's frustum.</summary>
    static readonly Vector3 RigOrigin = new Vector3(0f, -1000f, 0f);

    static GearPreview instance;

    class Slot
    {
        public Transform Model;
        public Camera View;
        public RenderTexture Texture;
        public Vector3 Pivot;
    }

    Slot weapon = new Slot();
    Slot armor = new Slot();

    /// <summary>Draws the weapon or the armour model into a slot rect.</summary>
    public static void Draw(Rect rect, bool isWeapon)
    {
        // Unity's fake-null: a plain ??= would keep a destroyed rig from a previous session.
        if (instance == null)
        {
            var host = new GameObject("GearPreview") { hideFlags = HideFlags.HideAndDontSave };
            instance = host.AddComponent<GearPreview>();
        }

        var texture = (isWeapon ? instance.weapon : instance.armor).Texture;
        if (texture != null)
        {
            GUI.DrawTexture(rect, texture, ScaleMode.ScaleToFit, true);
        }
    }

    void Awake()
    {
        transform.position = RigOrigin;
        Setup(weapon, Vector3.zero, 0.62f);
        Setup(armor, new Vector3(10f, 0f, 0f), 0.46f);
    }

    void OnDestroy()
    {
        Release(weapon);
        Release(armor);
    }

    void LateUpdate()
    {
        bool visible = PlayerInventory.WindowOpen;
        weapon.View.enabled = visible;
        armor.View.enabled = visible;
        if (!visible)
        {
            return;
        }

        BuildModels();

        // A slow turn plus a fixed tilt shows the depth of the blade and the breastplate.
        var spin = Quaternion.Euler(14f, Time.time * 45f, 0f);
        if (weapon.Model != null) weapon.Model.localRotation = spin * Quaternion.Euler(0f, 0f, 22f);
        if (armor.Model != null) armor.Model.localRotation = spin;
    }

    void Setup(Slot slot, Vector3 pivot, float size)
    {
        slot.Pivot = pivot;
        slot.Texture = new RenderTexture(160, 160, 16) { name = "GearPreview", antiAliasing = 2 };

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
    }

    static void Release(Slot slot)
    {
        if (slot.Texture != null)
        {
            slot.Texture.Release();
            Destroy(slot.Texture);
        }
    }

    void BuildModels()
    {
        if (weapon.Model == null)
        {
            var source = FindSword();
            if (source != null)
            {
                weapon.Model = Mount(Instantiate(source, transform), weapon.Pivot);
            }
        }

        if (armor.Model == null)
        {
            armor.Model = Mount(BuildArmor(), armor.Pivot);
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

    /// <summary>A breastplate: chest, collar, belt, pauldrons and a centre ridge.</summary>
    GameObject BuildArmor()
    {
        var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
        var plate = new Material(shader) { color = new Color(0.70f, 0.53f, 0.30f) };
        var trim = new Material(shader) { color = new Color(0.88f, 0.74f, 0.38f) };

        var root = new GameObject("ArmorModel").transform;
        root.SetParent(transform, false);

        Part(root, "Chest", new Vector3(0f, 0.02f, 0f), new Vector3(0.42f, 0.46f, 0.24f), plate);
        Part(root, "Ridge", new Vector3(0f, 0.02f, 0.12f), new Vector3(0.07f, 0.44f, 0.03f), trim);
        Part(root, "Collar", new Vector3(0f, 0.26f, 0f), new Vector3(0.3f, 0.07f, 0.26f), trim);
        Part(root, "Belt", new Vector3(0f, -0.2f, 0f), new Vector3(0.44f, 0.07f, 0.26f), trim);
        Part(root, "PauldronLeft", new Vector3(-0.27f, 0.17f, 0f), new Vector3(0.16f, 0.15f, 0.26f), plate);
        Part(root, "PauldronRight", new Vector3(0.27f, 0.17f, 0f), new Vector3(0.16f, 0.15f, 0.26f), plate);

        return root.gameObject;
    }

    static void Part(Transform parent, string name, Vector3 position, Vector3 scale, Material material)
    {
        var part = GameObject.CreatePrimitive(PrimitiveType.Cube);
        part.name = name;
        Destroy(part.GetComponent<Collider>());
        part.transform.SetParent(parent, false);
        part.transform.localPosition = position;
        part.transform.localScale = scale;
        part.GetComponent<Renderer>().sharedMaterial = material;
    }
}
