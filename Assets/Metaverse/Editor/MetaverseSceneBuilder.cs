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
    const string MaterialFolder = Root + "/Materials";

    [MenuItem("Tools/Metaverse/Build World Scene")]
    public static void Build()
    {
        EnsureFolders();

        var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

        BuildWorld();
        GameObject playerPrefab = BuildPlayerPrefab();
        BuildNetworking(playerPrefab);
        SetupCamera();
        SetupLight();

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene, ScenePath);

        // NetworkObject ids are derived from the asset/scene path, so they can only be
        // generated once the prefab and the scene exist on disk. Regenerate, then save again.
        RegenerateNetworkIds(playerPrefab);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene, ScenePath);

        RegisterScene();
        AssetDatabase.SaveAssets();

        Debug.Log($"Metaverse world built: {ScenePath}");
    }

    static void RegenerateNetworkIds(GameObject playerPrefab)
    {
        var onValidate = typeof(NetworkObject).GetMethod("OnValidate", BindingFlags.Instance | BindingFlags.NonPublic);
        if (onValidate == null)
        {
            Debug.LogError("NetworkObject.OnValidate not found; prefab ids will stay unset.");
            return;
        }

        // The scene must be in the asset database before its objects have a global id.
        AssetDatabase.ImportAsset(ScenePath, ImportAssetOptions.ForceUpdate);

        var prefabAsset = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        if (prefabAsset != null)
        {
            var networkObject = prefabAsset.GetComponent<NetworkObject>();
            onValidate.Invoke(networkObject, null);
            EditorUtility.SetDirty(prefabAsset);
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

        CreatePrimitive(PrimitiveType.Cylinder, "Plaza", world.transform, new Vector3(0f, 0.02f, 0f), new Vector3(20f, 0.02f, 20f), plazaMaterial);

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
    }

    static GameObject BuildPlayerPrefab()
    {
        var root = new GameObject("PlayerAvatar");

        Material shirtMaterial = CreateMaterial("PlayerShirt", Color.white);
        Material skinMaterial = CreateMaterial("PlayerSkin", new Color(0.95f, 0.79f, 0.66f));
        Material pantsMaterial = CreateMaterial("PlayerPants", new Color(0.22f, 0.26f, 0.36f));
        Material faceMaterial = CreateMaterial("PlayerFace", new Color(0.12f, 0.14f, 0.18f));

        // Blocky humanoid: torso, head, two arms and two legs, each limb on a shoulder/hip
        // pivot so it can swing while walking.
        var rig = new GameObject("Rig");
        rig.transform.SetParent(root.transform, false);

        var torso = BodyPart(PrimitiveType.Cube, "Torso", rig.transform, new Vector3(0f, 1.15f, 0f), new Vector3(0.62f, 0.72f, 0.34f), shirtMaterial);
        var head = BodyPart(PrimitiveType.Cube, "Head", rig.transform, new Vector3(0f, 1.73f, 0f), new Vector3(0.44f, 0.44f, 0.42f), skinMaterial);
        BodyPart(PrimitiveType.Cube, "Face", rig.transform, new Vector3(0f, 1.76f, 0.22f), new Vector3(0.3f, 0.1f, 0.03f), faceMaterial);

        var leftArm = Limb("LeftArm", rig.transform, new Vector3(-0.4f, 1.45f, 0f), new Vector3(0.18f, 0.62f, 0.24f), shirtMaterial);
        var rightArm = Limb("RightArm", rig.transform, new Vector3(0.4f, 1.45f, 0f), new Vector3(0.18f, 0.62f, 0.24f), shirtMaterial);
        var leftLeg = Limb("LeftLeg", rig.transform, new Vector3(-0.17f, 0.8f, 0f), new Vector3(0.24f, 0.8f, 0.26f), pantsMaterial);
        var rightLeg = Limb("RightLeg", rig.transform, new Vector3(0.17f, 0.8f, 0f), new Vector3(0.24f, 0.8f, 0.26f), pantsMaterial);

        var controller = root.AddComponent<CharacterController>();
        controller.height = 1.95f;
        controller.radius = 0.3f;
        controller.center = new Vector3(0f, 0.98f, 0f);
        controller.slopeLimit = 50f;
        controller.stepOffset = 0.4f;

        var limbAnimator = root.AddComponent<AvatarLimbAnimator>();
        limbAnimator.Rig = rig.transform;
        limbAnimator.LeftArm = leftArm;
        limbAnimator.RightArm = rightArm;
        limbAnimator.LeftLeg = leftLeg;
        limbAnimator.RightLeg = rightLeg;

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
            torso.GetComponent<Renderer>(),
            leftArm.GetComponentInChildren<Renderer>(),
            rightArm.GetComponentInChildren<Renderer>(),
        };
        avatar.NameTagHeight = head.transform.localPosition.y + 0.55f;

        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        Object.DestroyImmediate(root);
        return prefab;
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

    static void BuildNetworking(GameObject playerPrefab)
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
        manager.NetworkConfig.ConnectionApproval = false;
        // Required so clients register the in-scene placed ChatSystem object.
        manager.NetworkConfig.EnableSceneManagement = true;
        manager.RunInBackground = true;

        managerObject.AddComponent<MetaverseHUD>();

        var chatObject = new GameObject("ChatSystem");
        chatObject.AddComponent<NetworkObject>();
        chatObject.AddComponent<ChatSystem>();
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
