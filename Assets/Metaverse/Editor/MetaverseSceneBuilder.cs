using System.Collections.Generic;
using System.Reflection;
using Unity.Netcode;
using Unity.Netcode.Components;
using Unity.Netcode.Transports.UTP;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Generates the whole playable scene (world, player prefab, NetworkManager) so nothing
/// has to be wired by hand in the inspector. Re-runnable: it overwrites the generated assets.
/// </summary>
public static class MetaverseSceneBuilder
{
    const string Root = "Assets/Metaverse";
    const string ScenePath = Root + "/Scenes/Metaverse.unity";
    const string PrefabPath = Root + "/Prefabs/PlayerAvatar.prefab";
    const string MonsterPrefabPath = Root + "/Prefabs/Monster.prefab";
    const string NetworkPrefabsListPath = "Assets/DefaultNetworkPrefabs.asset";
    const string MaterialFolder = Root + "/Materials";

    // The hunting field is a second area of the same scene, far enough away that the
    // village never sees it. Warp pads are the only way across.
    static readonly Vector3 FieldCenter = new(120f, 0f, 0f);
    static readonly Vector3 VillagePad = new(10f, 0.05f, -12f);
    static readonly Vector3 FieldPad = new(120f, 0.05f, -25f);
    static readonly Vector3 VillageArrival = new(13.5f, 1f, -12f);
    static readonly Vector3 FieldArrival = new(120f, 1f, -21.5f);

    [MenuItem("Tools/Metaverse/Build World Scene")]
    public static void Build()
    {
        EnsureFolders();

        var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

        GameObject monsterPrefab = BuildMonsterPrefab();
        BuildWorld();
        BuildHuntingField(monsterPrefab);
        GameObject playerPrefab = BuildPlayerPrefab();
        BuildNetworking(playerPrefab, monsterPrefab);
        SetupCamera();
        SetupLight();

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene, ScenePath);

        // NetworkObject ids are derived from the asset/scene path, so they can only be
        // generated once the prefab and the scene exist on disk. Regenerate, then save again.
        RegenerateNetworkIds(playerPrefab, monsterPrefab);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene, ScenePath);

        RegisterScene();
        AssetDatabase.SaveAssets();

        Debug.Log($"Metaverse world built: {ScenePath}");
    }

    static void RegenerateNetworkIds(GameObject playerPrefab, GameObject monsterPrefab)
    {
        var onValidate = typeof(NetworkObject).GetMethod("OnValidate", BindingFlags.Instance | BindingFlags.NonPublic);
        if (onValidate == null)
        {
            Debug.LogError("NetworkObject.OnValidate not found; prefab ids will stay unset.");
            return;
        }

        // The scene must be in the asset database before its objects have a global id.
        AssetDatabase.ImportAsset(ScenePath, ImportAssetOptions.ForceUpdate);

        foreach (string path in new[] { PrefabPath, MonsterPrefabPath })
        {
            var prefabAsset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefabAsset != null)
            {
                var networkObject = prefabAsset.GetComponent<NetworkObject>();
                onValidate.Invoke(networkObject, null);
                EditorUtility.SetDirty(prefabAsset);
            }
        }

        foreach (var sceneObject in Object.FindObjectsByType<NetworkObject>(FindObjectsSortMode.None))
        {
            onValidate.Invoke(sceneObject, null);
            EditorUtility.SetDirty(sceneObject);
            uint hash = new SerializedObject(sceneObject).FindProperty("GlobalObjectIdHash").uintValue;
            Debug.Log($"[Metaverse] scene object {sceneObject.name} id={hash}");
        }

        AssetDatabase.SaveAssets();
    }

    static void EnsureFolders()
    {
        foreach (string folder in new[] { Root, Root + "/Scenes", Root + "/Prefabs", MaterialFolder })
        {
            if (!AssetDatabase.IsValidFolder(folder))
            {
                string parent = System.IO.Path.GetDirectoryName(folder).Replace('\\', '/');
                AssetDatabase.CreateFolder(parent, System.IO.Path.GetFileName(folder));
            }
        }
    }

    static void BuildWorld()
    {
        var world = new GameObject("World");

        Material groundMaterial = CreateMaterial("Ground", new Color(0.38f, 0.45f, 0.38f));
        Material plazaMaterial = CreateMaterial("Plaza", new Color(0.78f, 0.74f, 0.66f));
        Material wallMaterial = CreateMaterial("Wall", new Color(0.30f, 0.32f, 0.38f));
        Material buildingMaterial = CreateMaterial("Building", new Color(0.62f, 0.66f, 0.74f));
        Material accentMaterial = CreateMaterial("Accent", new Color(0.95f, 0.45f, 0.25f));

        var ground = CreatePrimitive(PrimitiveType.Plane, "Ground", world.transform, Vector3.zero, new Vector3(6f, 1f, 6f), groundMaterial);
        ground.isStatic = true;

        CreateDisc("Plaza", world.transform, new Vector3(0f, 0.02f, 0f), 20f, plazaMaterial);

        // Boundary walls so nobody walks off the world.
        const float half = 30f;
        CreatePrimitive(PrimitiveType.Cube, "WallNorth", world.transform, new Vector3(0f, 2f, half), new Vector3(60f, 4f, 1f), wallMaterial);
        CreatePrimitive(PrimitiveType.Cube, "WallSouth", world.transform, new Vector3(0f, 2f, -half), new Vector3(60f, 4f, 1f), wallMaterial);
        CreatePrimitive(PrimitiveType.Cube, "WallEast", world.transform, new Vector3(half, 2f, 0f), new Vector3(1f, 4f, 60f), wallMaterial);
        CreatePrimitive(PrimitiveType.Cube, "WallWest", world.transform, new Vector3(-half, 2f, 0f), new Vector3(1f, 4f, 60f), wallMaterial);

        // Buildings around the plaza.
        var buildings = new (Vector3 position, Vector3 size)[]
        {
            (new Vector3(-18f, 3f, 14f), new Vector3(10f, 6f, 8f)),
            (new Vector3(-6f, 4.5f, 20f), new Vector3(8f, 9f, 8f)),
            (new Vector3(14f, 3.5f, 16f), new Vector3(12f, 7f, 9f)),
            (new Vector3(20f, 5f, -6f), new Vector3(9f, 10f, 12f)),
            (new Vector3(-20f, 4f, -10f), new Vector3(8f, 8f, 10f)),
            (new Vector3(2f, 2.5f, -20f), new Vector3(14f, 5f, 8f)),
        };
        for (int i = 0; i < buildings.Length; i++)
        {
            CreatePrimitive(PrimitiveType.Cube, $"Building{i}", world.transform, buildings[i].position, buildings[i].size, buildingMaterial);
        }

        // Stair-style platforms to jump around on.
        for (int i = 0; i < 5; i++)
        {
            CreatePrimitive(PrimitiveType.Cube, $"Platform{i}",
                world.transform,
                new Vector3(-10f + i * 2.6f, 0.4f + i * 0.7f, -6f),
                new Vector3(2.4f, 0.5f, 2.4f),
                accentMaterial);
        }

        CreatePrimitive(PrimitiveType.Cylinder, "Monument", world.transform, new Vector3(0f, 3f, 0f), new Vector3(1.6f, 3f, 1.6f), accentMaterial);

        BuildShopNpc(world.transform);
        BuildWarpPad(world.transform, "WarpPadVillage", VillagePad, "Hunting Field", FieldArrival, accentMaterial, buildingMaterial);
    }

    /// <summary>Shopkeeper: the same humanoid the players use, standing on the plaza.</summary>
    static void BuildShopNpc(Transform parent)
    {
        Material coatMaterial = CreateMaterial("NpcCoat", new Color(0.35f, 0.45f, 0.72f));
        Material skinMaterial = CreateMaterial("PlayerSkin", new Color(0.95f, 0.79f, 0.66f));
        Material pantsMaterial = CreateMaterial("NpcPants", new Color(0.26f, 0.23f, 0.21f));
        Material faceMaterial = CreateMaterial("PlayerFace", new Color(0.12f, 0.14f, 0.18f));

        var npc = new GameObject("ShopNpc");
        npc.transform.SetParent(parent, false);
        npc.transform.localPosition = new Vector3(-9f, 0f, 7f);
        npc.transform.localRotation = Quaternion.LookRotation(new Vector3(9f, 0f, -7f));

        BuildHumanoid(npc.transform, coatMaterial, skinMaterial, pantsMaterial, faceMaterial);

        // The rig parts have no colliders, so give the shopkeeper one body to bump into.
        var body = npc.AddComponent<CapsuleCollider>();
        body.height = 1.95f;
        body.radius = 0.32f;
        body.center = new Vector3(0f, 0.98f, 0f);

        npc.AddComponent<ShopNpc>();
    }

    static void BuildWarpPad(Transform parent, string name, Vector3 position, string label, Vector3 destination, Material padMaterial, Material pillarMaterial)
    {
        var pad = new GameObject(name);
        pad.transform.SetParent(parent, false);
        pad.transform.localPosition = position;

        CreateDisc("Disc", pad.transform, Vector3.zero, 4f, padMaterial);
        CreatePrimitive(PrimitiveType.Cube, "PillarLeft", pad.transform, new Vector3(-2f, 1.5f, 0f), new Vector3(0.4f, 3f, 0.4f), pillarMaterial);
        CreatePrimitive(PrimitiveType.Cube, "PillarRight", pad.transform, new Vector3(2f, 1.5f, 0f), new Vector3(0.4f, 3f, 0.4f), pillarMaterial);

        var warp = pad.AddComponent<WarpPad>();
        warp.Destination = destination;
        warp.Label = label;
    }

    /// <summary>Second area of the scene: open ground, walls, monsters and the way home.</summary>
    static void BuildHuntingField(GameObject monsterPrefab)
    {
        var field = new GameObject("HuntingField");
        field.transform.position = FieldCenter;

        Material fieldMaterial = CreateMaterial("FieldGround", new Color(0.28f, 0.38f, 0.26f));
        Material rockMaterial = CreateMaterial("Rock", new Color(0.42f, 0.42f, 0.46f));
        Material wallMaterial = CreateMaterial("Wall", new Color(0.30f, 0.32f, 0.38f));
        Material accentMaterial = CreateMaterial("Accent", new Color(0.95f, 0.45f, 0.25f));
        Material buildingMaterial = CreateMaterial("Building", new Color(0.62f, 0.66f, 0.74f));

        var ground = CreatePrimitive(PrimitiveType.Plane, "FieldGround", field.transform, Vector3.zero, new Vector3(6f, 1f, 6f), fieldMaterial);
        ground.isStatic = true;

        const float half = 30f;
        CreatePrimitive(PrimitiveType.Cube, "FieldWallNorth", field.transform, new Vector3(0f, 2f, half), new Vector3(60f, 4f, 1f), wallMaterial);
        CreatePrimitive(PrimitiveType.Cube, "FieldWallSouth", field.transform, new Vector3(0f, 2f, -half), new Vector3(60f, 4f, 1f), wallMaterial);
        CreatePrimitive(PrimitiveType.Cube, "FieldWallEast", field.transform, new Vector3(half, 2f, 0f), new Vector3(1f, 4f, 60f), wallMaterial);
        CreatePrimitive(PrimitiveType.Cube, "FieldWallWest", field.transform, new Vector3(-half, 2f, 0f), new Vector3(1f, 4f, 60f), wallMaterial);

        var rocks = new (Vector3 position, float size)[]
        {
            (new Vector3(-14f, 0f, 9f), 3f),
            (new Vector3(11f, 0f, 14f), 4f),
            (new Vector3(18f, 0f, -8f), 2.5f),
            (new Vector3(-9f, 0f, -15f), 3.5f),
            (new Vector3(4f, 0f, 20f), 2f),
        };
        for (int i = 0; i < rocks.Length; i++)
        {
            CreatePrimitive(PrimitiveType.Cube, $"Rock{i}", field.transform,
                new Vector3(rocks[i].position.x, rocks[i].size * 0.35f, rocks[i].position.z),
                new Vector3(rocks[i].size, rocks[i].size * 0.7f, rocks[i].size),
                rockMaterial);
        }

        BuildWarpPad(field.transform, "WarpPadField", FieldPad - FieldCenter, "Village", VillageArrival, accentMaterial, buildingMaterial);

        var spawnerObject = new GameObject("MonsterSpawner");
        spawnerObject.transform.SetParent(field.transform, false);
        var spawner = spawnerObject.AddComponent<MonsterSpawner>();
        spawner.MonsterPrefab = monsterPrefab;
        spawner.Count = 12;
        spawner.Radius = 22f;
    }

    static GameObject BuildMonsterPrefab()
    {
        var root = new GameObject("Monster");

        Material bodyMaterial = CreateMaterial("MonsterBody", Color.white);
        Material eyeMaterial = CreateMaterial("MonsterEye", new Color(0.10f, 0.10f, 0.12f));

        var body = CreatePrimitive(PrimitiveType.Cube, "Body", root.transform, new Vector3(0f, 0.6f, 0f), new Vector3(1.1f, 1.2f, 1.1f), bodyMaterial);
        BodyPart(PrimitiveType.Cube, "EyeLeft", root.transform, new Vector3(-0.22f, 0.9f, 0.56f), new Vector3(0.16f, 0.16f, 0.05f), eyeMaterial);
        BodyPart(PrimitiveType.Cube, "EyeRight", root.transform, new Vector3(0.22f, 0.9f, 0.56f), new Vector3(0.16f, 0.16f, 0.05f), eyeMaterial);

        root.AddComponent<NetworkObject>();

        var networkTransform = root.AddComponent<NetworkTransform>();
        networkTransform.Interpolate = true;
        networkTransform.SyncScaleX = false;
        networkTransform.SyncScaleY = false;
        networkTransform.SyncScaleZ = false;

        var monster = root.AddComponent<Monster>();
        monster.ColoredParts = new[] { body.GetComponent<Renderer>() };

        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, MonsterPrefabPath);
        Object.DestroyImmediate(root);
        return prefab;
    }

    static GameObject BuildPlayerPrefab()
    {
        var root = new GameObject("PlayerAvatar");

        Material shirtMaterial = CreateMaterial("PlayerShirt", Color.white);
        Material skinMaterial = CreateMaterial("PlayerSkin", new Color(0.95f, 0.79f, 0.66f));
        Material pantsMaterial = CreateMaterial("PlayerPants", new Color(0.22f, 0.26f, 0.36f));
        Material faceMaterial = CreateMaterial("PlayerFace", new Color(0.12f, 0.14f, 0.18f));

        var humanoid = BuildHumanoid(root.transform, shirtMaterial, skinMaterial, pantsMaterial, faceMaterial);
        BuildSword(humanoid.RightArm);

        var controller = root.AddComponent<CharacterController>();
        controller.height = 1.95f;
        controller.radius = 0.3f;
        controller.center = new Vector3(0f, 0.98f, 0f);
        controller.slopeLimit = 50f;
        controller.stepOffset = 0.4f;

        var limbAnimator = root.AddComponent<AvatarLimbAnimator>();
        limbAnimator.Rig = humanoid.Rig;
        limbAnimator.LeftArm = humanoid.LeftArm;
        limbAnimator.RightArm = humanoid.RightArm;
        limbAnimator.LeftLeg = humanoid.LeftLeg;
        limbAnimator.RightLeg = humanoid.RightLeg;

        root.AddComponent<NetworkObject>();

        var networkTransform = root.AddComponent<NetworkTransform>();
        networkTransform.AuthorityMode = NetworkTransform.AuthorityModes.Owner;
        networkTransform.Interpolate = true;
        networkTransform.SyncScaleX = false;
        networkTransform.SyncScaleY = false;
        networkTransform.SyncScaleZ = false;

        var avatar = root.AddComponent<PlayerAvatar>();
        avatar.ColoredParts = new[]
        {
            humanoid.Torso.GetComponent<Renderer>(),
            humanoid.LeftArm.GetComponentInChildren<Renderer>(),
            humanoid.RightArm.GetComponentInChildren<Renderer>(),
        };
        avatar.NameTagHeight = humanoid.Head.transform.localPosition.y + 0.55f;

        root.AddComponent<PlayerStats>();
        root.AddComponent<PlayerCombat>();

        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        Object.DestroyImmediate(root);
        return prefab;
    }

    /// <summary>The parts of a built humanoid that callers need to wire up afterwards.</summary>
    struct Humanoid
    {
        public Transform Rig;
        public GameObject Torso;
        public GameObject Head;
        public Transform LeftArm;
        public Transform RightArm;
        public Transform LeftLeg;
        public Transform RightLeg;
    }

    /// <summary>
    /// Blocky humanoid: torso, head, two arms and two legs, each limb on a shoulder/hip
    /// pivot so it can swing while walking. Shared by the player avatar and the shopkeeper.
    /// </summary>
    static Humanoid BuildHumanoid(Transform parent, Material shirtMaterial, Material skinMaterial, Material pantsMaterial, Material faceMaterial)
    {
        var rig = new GameObject("Rig");
        rig.transform.SetParent(parent, false);

        var torso = BodyPart(PrimitiveType.Cube, "Torso", rig.transform, new Vector3(0f, 1.15f, 0f), new Vector3(0.62f, 0.72f, 0.34f), shirtMaterial);
        var head = BodyPart(PrimitiveType.Cube, "Head", rig.transform, new Vector3(0f, 1.73f, 0f), new Vector3(0.44f, 0.44f, 0.42f), skinMaterial);
        BodyPart(PrimitiveType.Cube, "Face", rig.transform, new Vector3(0f, 1.76f, 0.22f), new Vector3(0.3f, 0.1f, 0.03f), faceMaterial);

        return new Humanoid
        {
            Rig = rig.transform,
            Torso = torso,
            Head = head,
            LeftArm = Limb("LeftArm", rig.transform, new Vector3(-0.4f, 1.45f, 0f), new Vector3(0.18f, 0.62f, 0.24f), shirtMaterial),
            RightArm = Limb("RightArm", rig.transform, new Vector3(0.4f, 1.45f, 0f), new Vector3(0.18f, 0.62f, 0.24f), shirtMaterial),
            LeftLeg = Limb("LeftLeg", rig.transform, new Vector3(-0.17f, 0.8f, 0f), new Vector3(0.24f, 0.8f, 0.26f), pantsMaterial),
            RightLeg = Limb("RightLeg", rig.transform, new Vector3(0.17f, 0.8f, 0f), new Vector3(0.24f, 0.8f, 0.26f), pantsMaterial),
        };
    }

    /// <summary>
    /// Sword held in the right hand. It hangs under the arm pivot, so the limb animator's
    /// attack swing carries it along without any extra bone.
    /// </summary>
    static void BuildSword(Transform armPivot)
    {
        Material gripMaterial = CreateMaterial("SwordGrip", new Color(0.32f, 0.22f, 0.16f));
        Material bladeMaterial = CreateMaterial("SwordBlade", new Color(0.78f, 0.80f, 0.86f));

        var sword = new GameObject("Sword");
        sword.transform.SetParent(armPivot, false);
        sword.transform.localPosition = new Vector3(0f, -0.62f, 0.06f);
        // Tilted forward so the blade points ahead instead of dragging through the ground.
        sword.transform.localRotation = Quaternion.Euler(-40f, 0f, 0f);

        BodyPart(PrimitiveType.Cube, "Grip", sword.transform, new Vector3(0f, -0.05f, 0f), new Vector3(0.07f, 0.2f, 0.07f), gripMaterial);
        BodyPart(PrimitiveType.Cube, "Guard", sword.transform, new Vector3(0f, -0.16f, 0f), new Vector3(0.3f, 0.06f, 0.09f), bladeMaterial);
        BodyPart(PrimitiveType.Cube, "Blade", sword.transform, new Vector3(0f, -0.43f, 0f), new Vector3(0.11f, 0.5f, 0.04f), bladeMaterial);
    }

    /// <summary>Static body part with its primitive collider removed.</summary>
    static GameObject BodyPart(PrimitiveType type, string name, Transform parent, Vector3 localPosition, Vector3 localScale, Material material)
    {
        var part = CreatePrimitive(type, name, parent, localPosition, localScale, material);
        Object.DestroyImmediate(part.GetComponent<Collider>());
        return part;
    }

    /// <summary>Empty pivot at the shoulder/hip with the limb hanging below it, so rotating the pivot swings the limb.</summary>
    static Transform Limb(string name, Transform parent, Vector3 pivotPosition, Vector3 size, Material material)
    {
        var pivot = new GameObject(name);
        pivot.transform.SetParent(parent, false);
        pivot.transform.localPosition = pivotPosition;
        BodyPart(PrimitiveType.Cube, name + "Mesh", pivot.transform, new Vector3(0f, -size.y * 0.5f, 0f), size, material);
        return pivot.transform;
    }

    static void BuildNetworking(GameObject playerPrefab, GameObject monsterPrefab)
    {
        var managerObject = new GameObject("NetworkManager");
        var manager = managerObject.AddComponent<NetworkManager>();
        var transport = managerObject.AddComponent<UnityTransport>();

        transport.SetConnectionData("127.0.0.1", 7777);

        // A freshly added NetworkManager has not been through serialization yet, so the
        // config object can still be null here.
        manager.NetworkConfig ??= new NetworkConfig();

        manager.NetworkConfig.NetworkTransport = transport;
        manager.NetworkConfig.PlayerPrefab = playerPrefab;
        RegisterNetworkPrefab(manager, monsterPrefab);
        manager.NetworkConfig.ConnectionApproval = false;
        // Required so clients register the in-scene placed ChatSystem object.
        manager.NetworkConfig.EnableSceneManagement = true;
        manager.RunInBackground = true;

        managerObject.AddComponent<MetaverseHUD>();

        var chatObject = new GameObject("ChatSystem");
        chatObject.AddComponent<NetworkObject>();
        chatObject.AddComponent<ChatSystem>();
    }

    /// <summary>
    /// Monsters are spawned at runtime, so their prefab has to be in a prefab list the
    /// NetworkManager knows about. Netcode also auto-fills the default list on import,
    /// hence the contains check before adding.
    /// </summary>
    static void RegisterNetworkPrefab(NetworkManager manager, GameObject prefab)
    {
        var list = AssetDatabase.LoadAssetAtPath<NetworkPrefabsList>(NetworkPrefabsListPath);
        if (list == null)
        {
            Debug.LogWarning($"{NetworkPrefabsListPath} not found; register {prefab.name} by hand.");
            return;
        }

        if (!list.Contains(prefab))
        {
            list.Add(new NetworkPrefab { Prefab = prefab });
            EditorUtility.SetDirty(list);
        }

        if (!manager.NetworkConfig.Prefabs.NetworkPrefabsLists.Contains(list))
        {
            manager.NetworkConfig.Prefabs.NetworkPrefabsLists.Add(list);
        }
    }

    static void SetupCamera()
    {
        var camera = Camera.main;
        if (camera == null)
        {
            var cameraObject = new GameObject("Main Camera") { tag = "MainCamera" };
            camera = cameraObject.AddComponent<Camera>();
        }

        camera.transform.SetPositionAndRotation(new Vector3(0f, 8f, -12f), Quaternion.Euler(20f, 0f, 0f));
        camera.farClipPlane = 300f;

        if (camera.GetComponent<FollowCamera>() == null)
        {
            camera.gameObject.AddComponent<FollowCamera>();
        }
    }

    static void SetupLight()
    {
        var light = Object.FindFirstObjectByType<Light>();
        if (light != null && light.type == LightType.Directional)
        {
            light.transform.rotation = Quaternion.Euler(48f, 35f, 0f);
            light.intensity = 1.2f;
            light.shadows = LightShadows.Soft;
        }
    }

    static GameObject CreatePrimitive(PrimitiveType type, string name, Transform parent, Vector3 localPosition, Vector3 localScale, Material material)
    {
        var instance = GameObject.CreatePrimitive(type);
        instance.name = name;
        instance.transform.SetParent(parent, false);
        instance.transform.localPosition = localPosition;
        instance.transform.localScale = localScale;
        instance.GetComponent<MeshRenderer>().sharedMaterial = material;
        return instance;
    }

    /// <summary>
    /// Flat decorative disc. A cylinder primitive ships with a CapsuleCollider whose height is
    /// clamped to twice its radius, which would turn a thin disc into an invisible dome, so the
    /// collider goes away and the ground below carries the collision.
    /// </summary>
    static GameObject CreateDisc(string name, Transform parent, Vector3 localPosition, float diameter, Material material)
    {
        var disc = CreatePrimitive(PrimitiveType.Cylinder, name, parent, localPosition, new Vector3(diameter, 0.02f, diameter), material);
        Object.DestroyImmediate(disc.GetComponent<Collider>());
        return disc;
    }

    static readonly Dictionary<string, Material> materialCache = new();

    static Material CreateMaterial(string name, Color color)
    {
        if (materialCache.TryGetValue(name, out var cached) && cached != null)
        {
            return cached;
        }

        string path = $"{MaterialFolder}/{name}.mat";
        var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);

        if (material == null)
        {
            material = new Material(shader);
            AssetDatabase.CreateAsset(material, path);
        }

        material.shader = shader;
        material.color = color;
        if (material.HasProperty("_BaseColor"))
        {
            material.SetColor("_BaseColor", color);
        }
        if (material.HasProperty("_Smoothness"))
        {
            material.SetFloat("_Smoothness", 0.15f);
        }

        EditorUtility.SetDirty(material);
        materialCache[name] = material;
        return material;
    }

    static void RegisterScene()
    {
        var scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
        scenes.RemoveAll(s => s.path == ScenePath);
        scenes.Insert(0, new EditorBuildSettingsScene(ScenePath, true));
        EditorBuildSettings.scenes = scenes.ToArray();
    }
}
