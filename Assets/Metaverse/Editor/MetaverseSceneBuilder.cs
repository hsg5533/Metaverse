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

    // Fourth area: the duelling arena.
    static readonly Vector3 ArenaCentre = new(0f, 0f, 120f);
    const float ArenaRadius = 22f;
    static readonly Vector3 CornerA = new(0f, 1f, 106f);
    static readonly Vector3 CornerB = new(0f, 1f, 134f);
    static readonly Vector3 VillageArenaPad = new(0f, 0.05f, -13f);
    static readonly Vector3 ArenaPad = new(0f, 0.05f, 96f);
    static readonly Vector3 VillageArenaArrival = new(0f, 1f, -9.5f);
    static readonly Vector3 ArenaArrival = new(0f, 1f, 99.5f);

    // Third area: the boss dungeon, gated behind a level requirement.
    const int DungeonLevel = 5;
    static readonly Vector3 DungeonCenter = new(-120f, 0f, 0f);
    static readonly Vector3 VillageDungeonPad = new(-10f, 0.05f, -12f);
    static readonly Vector3 DungeonPad = new(-120f, 0.05f, -26f);
    static readonly Vector3 VillageDungeonArrival = new(-13.5f, 1f, -12f);
    static readonly Vector3 DungeonArrival = new(-120f, 1f, -22.5f);

    [MenuItem("Tools/Metaverse/Build World Scene")]
    public static void Build()
    {
        EnsureFolders();

        var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

        GameObject monsterPrefab = BuildMonsterPrefab();
        BuildWorld();
        BuildHuntingField(monsterPrefab);
        BuildDungeon(monsterPrefab);
        BuildArena();
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

        // Houses: walls, a roof that overhangs, a door and lit windows. Nothing else in the
        // world has this silhouette, so a house never reads as a rock or a resource.
        var houses = new (Vector3 position, Vector3 size)[]
        {
            (new Vector3(-18f, 0f, 14f), new Vector3(10f, 6f, 8f)),
            (new Vector3(-6f, 0f, 20f), new Vector3(8f, 9f, 8f)),
            (new Vector3(14f, 0f, 16f), new Vector3(12f, 7f, 9f)),
            (new Vector3(20f, 0f, -6f), new Vector3(9f, 10f, 12f)),
            (new Vector3(-20f, 0f, -10f), new Vector3(8f, 8f, 10f)),
            (new Vector3(2f, 0f, -20f), new Vector3(14f, 5f, 8f)),
        };
        for (int i = 0; i < houses.Length; i++)
        {
            BuildHouse(world.transform, $"House{i}", houses[i].position, houses[i].size);
        }

        BuildPlatforms(world.transform);
        BuildMonument(world.transform, accentMaterial, buildingMaterial);

        BuildShopNpc(world.transform);
        BuildWarpPad(world.transform, "WarpPadVillage", VillagePad, "Hunting Field", FieldArrival, "PortalField", new Color(0.98f, 0.55f, 0.20f));

        var dungeonPad = BuildWarpPad(world.transform, "WarpPadDungeon", VillageDungeonPad, "Dungeon", DungeonArrival, "PortalDungeon", new Color(0.65f, 0.30f, 0.90f));
        dungeonPad.RequiredLevel = DungeonLevel;

        BuildWarpPad(world.transform, "WarpPadArena", VillageArenaPad, "Arena", ArenaArrival, "PortalArena", new Color(0.90f, 0.30f, 0.35f));

        BuildStations(world.transform);
        BuildVillageNodes(world.transform);
    }


    /// <summary>A house: walls, overhanging roof, door and windows.</summary>
    static void BuildHouse(Transform parent, string name, Vector3 groundPosition, Vector3 size)
    {
        Material wallMaterial = CreateMaterial("HouseWall", new Color(0.82f, 0.78f, 0.70f));
        Material beamMaterial = CreateMaterial("HouseBeam", new Color(0.42f, 0.30f, 0.20f));
        Material roofMaterial = CreateMaterial("HouseRoof", new Color(0.52f, 0.24f, 0.20f));
        Material windowMaterial = CreateMaterial("HouseWindow", new Color(0.98f, 0.86f, 0.45f));

        var house = new GameObject(name);
        house.transform.SetParent(parent, false);
        house.transform.localPosition = groundPosition;

        CreatePrimitive(PrimitiveType.Cube, "Walls", house.transform, new Vector3(0f, size.y * 0.5f, 0f), size, wallMaterial);

        // Roof: two overhanging slabs leaning against each other.
        float roofY = size.y + 0.35f;
        var roofLeft = CreatePrimitive(PrimitiveType.Cube, "RoofLeft", house.transform, new Vector3(-size.x * 0.22f, roofY, 0f), new Vector3(size.x * 0.62f, 0.3f, size.z + 1.2f), roofMaterial);
        roofLeft.transform.localRotation = Quaternion.Euler(0f, 0f, 22f);
        var roofRight = CreatePrimitive(PrimitiveType.Cube, "RoofRight", house.transform, new Vector3(size.x * 0.22f, roofY, 0f), new Vector3(size.x * 0.62f, 0.3f, size.z + 1.2f), roofMaterial);
        roofRight.transform.localRotation = Quaternion.Euler(0f, 0f, -22f);

        // Corner beams, so the walls do not read as one flat block.
        float halfX = size.x * 0.5f;
        float halfZ = size.z * 0.5f;
        foreach (var corner in new[] { new Vector2(-halfX, -halfZ), new Vector2(halfX, -halfZ), new Vector2(-halfX, halfZ), new Vector2(halfX, halfZ) })
        {
            CreatePrimitive(PrimitiveType.Cube, "Beam", house.transform, new Vector3(corner.x, size.y * 0.5f, corner.y), new Vector3(0.35f, size.y, 0.35f), beamMaterial);
        }

        // Door and windows on the plaza facing side.
        float front = -halfZ - 0.06f;
        BodyPart(PrimitiveType.Cube, "Door", house.transform, new Vector3(0f, 1.1f, front), new Vector3(1.3f, 2.2f, 0.15f), beamMaterial);
        BodyPart(PrimitiveType.Cube, "WindowLeft", house.transform, new Vector3(-size.x * 0.28f, size.y * 0.6f, front), new Vector3(1.1f, 1.1f, 0.15f), windowMaterial);
        BodyPart(PrimitiveType.Cube, "WindowRight", house.transform, new Vector3(size.x * 0.28f, size.y * 0.6f, front), new Vector3(1.1f, 1.1f, 0.15f), windowMaterial);
    }

    /// <summary>Obelisk on a stepped base, the landmark at the centre of the plaza.</summary>
    static void BuildMonument(Transform parent, Material accentMaterial, Material stoneMaterial)
    {
        var monument = new GameObject("Monument");
        monument.transform.SetParent(parent, false);

        CreatePrimitive(PrimitiveType.Cube, "BaseLower", monument.transform, new Vector3(0f, 0.25f, 0f), new Vector3(4f, 0.5f, 4f), stoneMaterial);
        CreatePrimitive(PrimitiveType.Cube, "BaseUpper", monument.transform, new Vector3(0f, 0.75f, 0f), new Vector3(2.8f, 0.5f, 2.8f), stoneMaterial);
        CreatePrimitive(PrimitiveType.Cube, "Shaft", monument.transform, new Vector3(0f, 3.5f, 0f), new Vector3(1.2f, 5f, 1.2f), stoneMaterial);
        var tip = CreatePrimitive(PrimitiveType.Cube, "Tip", monument.transform, new Vector3(0f, 6.4f, 0f), new Vector3(0.9f, 0.9f, 0.9f), accentMaterial);
        tip.transform.localRotation = Quaternion.Euler(45f, 0f, 45f);
    }

    /// <summary>Platforms south of the plaza to jump across. No rules, just a climb.</summary>
    static void BuildPlatforms(Transform parent)
    {
        Material stepMaterial = CreateMaterial("Platform", new Color(0.90f, 0.62f, 0.25f));
        Material topMaterial = CreateMaterial("PlatformTop", new Color(0.30f, 0.75f, 0.45f));

        var course = new GameObject("Platforms");
        course.transform.SetParent(parent, false);

        var steps = new[]
        {
            new Vector3(-11.6f, 0.6f, -6f),
            new Vector3(-8.8f, 1.1f, -7.6f),
            new Vector3(-6f, 1.6f, -6f),
            new Vector3(-3.2f, 2.1f, -7.6f),
            new Vector3(-0.4f, 2.6f, -6f),
            new Vector3(2.4f, 3.1f, -7.6f),
            new Vector3(5.2f, 3.6f, -6f),
            new Vector3(8f, 4f, -7.2f),
        };

        for (int i = 0; i < steps.Length; i++)
        {
            CreatePrimitive(PrimitiveType.Cube, $"Platform{i}", course.transform, steps[i], new Vector3(2.2f, 0.4f, 2.2f), stepMaterial);
        }

        // A wider platform at the top, worth the climb.
        CreatePrimitive(PrimitiveType.Cube, "PlatformTop", course.transform, new Vector3(11.4f, 4.2f, -6.6f), new Vector3(3.2f, 0.4f, 3.2f), topMaterial);
    }



    /// <summary>
    /// Fourth area: the duelling arena. A raised ring with a low wall, two corner marks and
    /// seating around it. <see cref="DuelSystem"/> only lets a match start inside the ring.
    /// </summary>
    static void BuildArena()
    {
        var arena = new GameObject("Arena");
        arena.transform.position = ArenaCentre;

        Material sandMaterial = CreateMaterial("ArenaSand", new Color(0.72f, 0.62f, 0.44f));
        Material stoneMaterial = CreateMaterial("ArenaStone", new Color(0.52f, 0.50f, 0.48f));
        Material trimMaterial = CreateMaterial("ArenaTrim", new Color(0.36f, 0.34f, 0.34f));
        Material wallMaterial = CreateMaterial("Wall", new Color(0.30f, 0.32f, 0.38f));
        Material redMaterial = CreateMaterial("CornerRed", new Color(0.85f, 0.28f, 0.28f));
        Material blueMaterial = CreateMaterial("CornerBlue", new Color(0.30f, 0.45f, 0.88f));
        Material torchMaterial = CreateMaterial("Torch", new Color(0.98f, 0.65f, 0.25f));

        var ground = CreatePrimitive(PrimitiveType.Plane, "ArenaGround", arena.transform, Vector3.zero, new Vector3(6f, 1f, 6f), stoneMaterial);
        ground.isStatic = true;

        const float half = 30f;
        CreatePrimitive(PrimitiveType.Cube, "ArenaWallNorth", arena.transform, new Vector3(0f, 2f, half), new Vector3(60f, 4f, 1f), wallMaterial);
        CreatePrimitive(PrimitiveType.Cube, "ArenaWallSouth", arena.transform, new Vector3(0f, 2f, -half), new Vector3(60f, 4f, 1f), wallMaterial);
        CreatePrimitive(PrimitiveType.Cube, "ArenaWallEast", arena.transform, new Vector3(half, 2f, 0f), new Vector3(1f, 4f, 60f), wallMaterial);
        CreatePrimitive(PrimitiveType.Cube, "ArenaWallWest", arena.transform, new Vector3(-half, 2f, 0f), new Vector3(1f, 4f, 60f), wallMaterial);

        // The floor of the ring, a low step up so it reads as a stage.
        CreatePrimitive(PrimitiveType.Cube, "Ring", arena.transform, new Vector3(0f, 0.17f, 0f), new Vector3(ArenaRadius * 2f, 0.35f, ArenaRadius * 2f), sandMaterial);
        CreateDisc("RingMark", arena.transform, new Vector3(0f, 0.36f, 0f), ArenaRadius * 1.9f, sandMaterial);
        CreateDisc("Centre", arena.transform, new Vector3(0f, 0.37f, 0f), 4f, trimMaterial);

        // Low wall around the ring: twelve segments turned to follow the circle.
        for (int i = 0; i < 12; i++)
        {
            float angle = i * Mathf.PI * 2f / 12f;
            var segment = CreatePrimitive(PrimitiveType.Cube, $"RingWall{i}", arena.transform,
                new Vector3(Mathf.Cos(angle) * ArenaRadius, 0.85f, Mathf.Sin(angle) * ArenaRadius),
                new Vector3(ArenaRadius * 0.58f, 1f, 0.6f), trimMaterial);
            segment.transform.localRotation = Quaternion.Euler(0f, -angle * Mathf.Rad2Deg + 90f, 0f);
        }

        // Corner marks the duel starts on.
        CreateDisc("CornerA", arena.transform, CornerA - ArenaCentre + new Vector3(0f, -0.62f, 0f), 4f, redMaterial);
        CreateDisc("CornerB", arena.transform, CornerB - ArenaCentre + new Vector3(0f, -0.62f, 0f), 4f, blueMaterial);

        // Seating on the east and west sides, three tiers each.
        foreach (float side in new[] { -1f, 1f })
        {
            for (int tier = 0; tier < 3; tier++)
            {
                CreatePrimitive(PrimitiveType.Cube, side < 0f ? $"StandWest{tier}" : $"StandEast{tier}", arena.transform,
                    new Vector3(side * (ArenaRadius + 2.5f + tier * 2f), 0.4f + tier * 0.8f, 0f),
                    new Vector3(2f, 0.8f + tier * 1.6f, ArenaRadius * 1.6f), stoneMaterial);
            }
        }

        // Torch posts at the four compass points.
        for (int i = 0; i < 4; i++)
        {
            float angle = i * Mathf.PI * 0.5f + Mathf.PI * 0.25f;
            var post = new GameObject($"Torch{i}");
            post.transform.SetParent(arena.transform, false);
            post.transform.localPosition = new Vector3(Mathf.Cos(angle) * (ArenaRadius + 1.2f), 0f, Mathf.Sin(angle) * (ArenaRadius + 1.2f));
            CreatePrimitive(PrimitiveType.Cube, "Post", post.transform, new Vector3(0f, 1.6f, 0f), new Vector3(0.35f, 3.2f, 0.35f), trimMaterial);
            BodyPart(PrimitiveType.Cube, "Flame", post.transform, new Vector3(0f, 3.5f, 0f), new Vector3(0.5f, 0.6f, 0.5f), torchMaterial);
        }

        BuildWarpPad(arena.transform, "WarpPadArenaExit", ArenaPad - ArenaCentre, "Village", VillageArenaArrival, "PortalVillage", new Color(0.30f, 0.80f, 0.85f));

        var duelObject = new GameObject("DuelSystem");
        duelObject.transform.SetParent(arena.transform, false);
        duelObject.AddComponent<NetworkObject>();
        var duels = duelObject.AddComponent<DuelSystem>();
        duels.ArenaCentre = ArenaCentre;
        duels.ArenaRadius = ArenaRadius;
        duels.CornerA = CornerA;
        duels.CornerB = CornerB;
    }


    /// <summary>
    /// A chest: a box with a hinged lid. The lid hangs off its own pivot at the back edge,
    /// so rotating that pivot opens it rather than sinking it into the body.
    /// </summary>
    static void BuildChest(Transform parent, string name, Vector3 localPosition)
    {
        Material woodMaterial = CreateMaterial("ChestWood", new Color(0.42f, 0.28f, 0.16f));
        Material bandMaterial = CreateMaterial("ChestBand", new Color(0.80f, 0.66f, 0.28f));

        var chest = new GameObject(name);
        chest.transform.SetParent(parent, false);
        chest.transform.localPosition = localPosition;

        CreatePrimitive(PrimitiveType.Cube, "Body", chest.transform, new Vector3(0f, 0.3f, 0f), new Vector3(1.4f, 0.6f, 0.9f), woodMaterial);
        BodyPart(PrimitiveType.Cube, "BandLeft", chest.transform, new Vector3(-0.45f, 0.3f, 0f), new Vector3(0.12f, 0.64f, 0.94f), bandMaterial);
        BodyPart(PrimitiveType.Cube, "BandRight", chest.transform, new Vector3(0.45f, 0.3f, 0f), new Vector3(0.12f, 0.64f, 0.94f), bandMaterial);
        BodyPart(PrimitiveType.Cube, "Lock", chest.transform, new Vector3(0f, 0.42f, -0.47f), new Vector3(0.22f, 0.22f, 0.08f), bandMaterial);

        var lid = new GameObject("Lid");
        lid.transform.SetParent(chest.transform, false);
        lid.transform.localPosition = new Vector3(0f, 0.6f, 0.45f);
        BodyPart(PrimitiveType.Cube, "LidBoard", lid.transform, new Vector3(0f, 0.1f, -0.45f), new Vector3(1.4f, 0.2f, 0.9f), woodMaterial);
        BodyPart(PrimitiveType.Cube, "LidBand", lid.transform, new Vector3(0f, 0.21f, -0.45f), new Vector3(0.2f, 0.06f, 0.94f), bandMaterial);

        chest.AddComponent<NetworkObject>();
        var treasure = chest.AddComponent<TreasureChest>();
        treasure.Lid = lid.transform;
    }

    /// <summary>Anvil, campfire and quest board, all within sight of the plaza.</summary>
    static void BuildStations(Transform parent)
    {
        Material woodMaterial = CreateMaterial("Wood", new Color(0.45f, 0.32f, 0.20f));
        Material emberMaterial = CreateMaterial("Ember", new Color(0.95f, 0.55f, 0.20f));

        BuildAnvil(parent, new Vector3(-5f, 0f, 10f), woodMaterial);
        BuildCampfire(parent, new Vector3(6f, 0f, 9f), woodMaterial, emberMaterial);

        var board = new GameObject("QuestBoard");
        board.transform.SetParent(parent, false);
        board.transform.localPosition = new Vector3(0f, 0f, 12f);
        CreatePrimitive(PrimitiveType.Cube, "PostLeft", board.transform, new Vector3(-0.9f, 0.7f, 0f), new Vector3(0.15f, 1.4f, 0.15f), woodMaterial);
        CreatePrimitive(PrimitiveType.Cube, "PostRight", board.transform, new Vector3(0.9f, 0.7f, 0f), new Vector3(0.15f, 1.4f, 0.15f), woodMaterial);
        CreatePrimitive(PrimitiveType.Cube, "Board", board.transform, new Vector3(0f, 1.5f, 0f), new Vector3(2.2f, 1.2f, 0.12f), woodMaterial);
        var questBoard = board.AddComponent<QuestBoard>();
        questBoard.Title = "Quest Board";
        questBoard.PanelSize = new Vector2(360f, 240f);
    }


    /// <summary>
    /// A smithy: anvil on a stump with a horn and a hammer lying on it, a tool rack behind
    /// and a stack of ingots. The shape says "craft here" without needing the label.
    /// </summary>
    static void BuildAnvil(Transform parent, Vector3 localPosition, Material woodMaterial)
    {
        Material ironMaterial = CreateMaterial("AnvilIron", new Color(0.26f, 0.27f, 0.31f));
        Material handleMaterial = CreateMaterial("ToolHandle", new Color(0.52f, 0.36f, 0.22f));
        Material ingotMaterial = CreateMaterial("Ingot", new Color(0.74f, 0.62f, 0.36f));

        var anvil = new GameObject("Anvil");
        anvil.transform.SetParent(parent, false);
        anvil.transform.localPosition = localPosition;

        // Stump and the anvil itself: wide foot, pinched waist, wider top plate, pointed horn.
        CreatePrimitive(PrimitiveType.Cube, "Stump", anvil.transform, new Vector3(0f, 0.35f, 0f), new Vector3(1f, 0.7f, 1f), woodMaterial);
        CreatePrimitive(PrimitiveType.Cube, "Foot", anvil.transform, new Vector3(0f, 0.79f, 0f), new Vector3(0.9f, 0.18f, 0.5f), ironMaterial);
        CreatePrimitive(PrimitiveType.Cube, "Waist", anvil.transform, new Vector3(0f, 0.99f, 0f), new Vector3(0.45f, 0.24f, 0.36f), ironMaterial);
        CreatePrimitive(PrimitiveType.Cube, "Plate", anvil.transform, new Vector3(0f, 1.19f, 0f), new Vector3(1.15f, 0.17f, 0.55f), ironMaterial);
        var horn = BodyPart(PrimitiveType.Cube, "Horn", anvil.transform, new Vector3(0.78f, 1.19f, 0f), new Vector3(0.55f, 0.13f, 0.24f), ironMaterial);
        horn.transform.localRotation = Quaternion.Euler(0f, 0f, -6f);

        // Hammer resting across the plate.
        var handle = BodyPart(PrimitiveType.Cube, "HammerHandle", anvil.transform, new Vector3(-0.28f, 1.32f, 0.05f), new Vector3(0.07f, 0.5f, 0.07f), handleMaterial);
        handle.transform.localRotation = Quaternion.Euler(0f, 20f, 78f);
        BodyPart(PrimitiveType.Cube, "HammerHead", anvil.transform, new Vector3(-0.02f, 1.34f, 0.02f), new Vector3(0.2f, 0.17f, 0.17f), ironMaterial);

        // Tool rack behind, with three blanks hanging from the crossbar.
        CreatePrimitive(PrimitiveType.Cube, "RackPostLeft", anvil.transform, new Vector3(-1f, 0.9f, -0.9f), new Vector3(0.12f, 1.8f, 0.12f), woodMaterial);
        CreatePrimitive(PrimitiveType.Cube, "RackPostRight", anvil.transform, new Vector3(1f, 0.9f, -0.9f), new Vector3(0.12f, 1.8f, 0.12f), woodMaterial);
        BodyPart(PrimitiveType.Cube, "RackBar", anvil.transform, new Vector3(0f, 1.7f, -0.9f), new Vector3(2.1f, 0.12f, 0.12f), woodMaterial);
        for (int i = 0; i < 3; i++)
        {
            BodyPart(PrimitiveType.Cube, $"Tool{i}", anvil.transform, new Vector3(-0.6f + i * 0.6f, 1.3f, -0.9f), new Vector3(0.1f, 0.7f, 0.06f), ironMaterial);
        }

        // Ingots stacked next to the stump.
        for (int i = 0; i < 3; i++)
        {
            BodyPart(PrimitiveType.Cube, $"Ingot{i}", anvil.transform, new Vector3(-1.3f, 0.09f + i * 0.16f, 0.35f - i * 0.05f), new Vector3(0.5f, 0.15f, 0.28f), ingotMaterial);
        }

        var station = anvil.AddComponent<CraftStation>();
        station.Title = "Anvil";
        station.PromptHeight = 2.1f;
    }

    /// <summary>
    /// A cooking fire: ring of stones, crossed logs, layered flames, a pot on a tripod and
    /// two logs to sit on. Reads as a place to cook rather than a lit box.
    /// </summary>
    static void BuildCampfire(Transform parent, Vector3 localPosition, Material woodMaterial, Material emberMaterial)
    {
        Material stoneMaterial = CreateMaterial("FireStone", new Color(0.50f, 0.50f, 0.53f));
        Material flameMaterial = CreateMaterial("FlameCore", new Color(1f, 0.86f, 0.38f));
        Material potMaterial = CreateMaterial("CookPot", new Color(0.20f, 0.20f, 0.23f));

        var fire = new GameObject("Campfire");
        fire.transform.SetParent(parent, false);
        fire.transform.localPosition = localPosition;

        // Stone ring.
        for (int i = 0; i < 8; i++)
        {
            float angle = i * Mathf.PI * 2f / 8f;
            var stone = CreatePrimitive(PrimitiveType.Sphere, $"Stone{i}", fire.transform,
                new Vector3(Mathf.Cos(angle) * 1.05f, 0.16f, Mathf.Sin(angle) * 1.05f),
                new Vector3(0.42f, 0.32f, 0.42f), stoneMaterial);
            stone.transform.localRotation = Quaternion.Euler(0f, i * 31f, 0f);
        }

        // Crossed logs in the middle.
        for (int i = 0; i < 3; i++)
        {
            var log = BodyPart(PrimitiveType.Cylinder, $"Log{i}", fire.transform, new Vector3(0f, 0.3f, 0f), new Vector3(0.16f, 0.6f, 0.16f), woodMaterial);
            log.transform.localRotation = Quaternion.Euler(62f, i * 60f, 0f);
        }

        // Flames: three shrinking blocks, turned so they never look like a stacked box.
        var flameLow = BodyPart(PrimitiveType.Cube, "FlameLow", fire.transform, new Vector3(0f, 0.55f, 0f), new Vector3(0.7f, 0.7f, 0.7f), emberMaterial);
        flameLow.transform.localRotation = Quaternion.Euler(0f, 25f, 0f);
        var flameMid = BodyPart(PrimitiveType.Cube, "FlameMid", fire.transform, new Vector3(0.06f, 0.95f, -0.04f), new Vector3(0.48f, 0.55f, 0.48f), emberMaterial);
        flameMid.transform.localRotation = Quaternion.Euler(0f, -15f, 8f);
        var flameTip = BodyPart(PrimitiveType.Cube, "FlameTip", fire.transform, new Vector3(-0.05f, 1.28f, 0.05f), new Vector3(0.28f, 0.38f, 0.28f), flameMaterial);
        flameTip.transform.localRotation = Quaternion.Euler(0f, 40f, -10f);

        // Tripod with a pot hanging over the fire.
        for (int i = 0; i < 3; i++)
        {
            float angle = i * Mathf.PI * 2f / 3f;
            var leg = BodyPart(PrimitiveType.Cube, $"TripodLeg{i}", fire.transform,
                new Vector3(Mathf.Cos(angle) * 0.55f, 0.9f, Mathf.Sin(angle) * 0.55f),
                new Vector3(0.09f, 1.9f, 0.09f), woodMaterial);
            leg.transform.localRotation = Quaternion.Euler(Mathf.Sin(angle) * 18f, 0f, -Mathf.Cos(angle) * 18f);
        }

        BodyPart(PrimitiveType.Sphere, "Pot", fire.transform, new Vector3(0f, 1.05f, 0f), new Vector3(0.62f, 0.5f, 0.62f), potMaterial);
        BodyPart(PrimitiveType.Cube, "PotRim", fire.transform, new Vector3(0f, 1.28f, 0f), new Vector3(0.66f, 0.08f, 0.66f), potMaterial);

        // Two logs to sit on.
        foreach (float side in new[] { -1f, 1f })
        {
            var seat = CreatePrimitive(PrimitiveType.Cylinder, side < 0f ? "SeatLeft" : "SeatRight", fire.transform,
                new Vector3(side * 2f, 0.25f, side * 0.4f), new Vector3(0.5f, 1.1f, 0.5f), woodMaterial);
            seat.transform.localRotation = Quaternion.Euler(90f, side * 20f, 0f);
        }

        var station = fire.AddComponent<Campfire>();
        station.Title = "Campfire";
        station.PromptHeight = 2.2f;
    }

    /// <summary>Herbs and trees around the village; most of the ore sits out in the field.</summary>
    static void BuildVillageNodes(Transform parent)
    {
        var herbs = new[]
        {
            new Vector3(-12f, 0f, 2f), new Vector3(10f, 0f, 4f),
            new Vector3(-4f, 0f, -10f), new Vector3(6f, 0f, -14f),
            new Vector3(-16f, 0f, 8f),
        };
        for (int i = 0; i < herbs.Length; i++)
        {
            BuildGatherNode(parent, "Herb" + i, herbs[i], GatherKind.Herb);
        }

        var trees = new[]
        {
            new Vector3(18f, 0f, 4f), new Vector3(-16f, 0f, -2f),
            new Vector3(4f, 0f, 17f), new Vector3(-11f, 0f, 13f),
        };
        for (int i = 0; i < trees.Length; i++)
        {
            BuildGatherNode(parent, "Tree" + i, trees[i], GatherKind.Wood);
        }

        BuildGatherNode(parent, "VillageOre0", new Vector3(22f, 0f, 8f), GatherKind.Ore);
        BuildGatherNode(parent, "VillageOre1", new Vector3(-26f, 0f, -18f), GatherKind.Ore);
    }

    /// <summary>
    /// One harvestable node. These are in-scene NetworkObjects, so the server owns the
    /// cooldown and every client sees the same rock disappear and come back.
    /// </summary>
    static void BuildGatherNode(Transform parent, string name, Vector3 localPosition, GatherKind kind)
    {
        Material oreStoneMaterial = CreateMaterial("OreStone", new Color(0.26f, 0.25f, 0.30f));
        Material oreCrystalMaterial = CreateMaterial("OreCrystal", new Color(0.35f, 0.85f, 0.95f));
        Material herbMaterial = CreateMaterial("HerbLeaf", new Color(0.35f, 0.72f, 0.38f));
        Material flowerMaterial = CreateMaterial("HerbFlower", new Color(0.95f, 0.75f, 0.30f));
        Material trunkMaterial = CreateMaterial("TreeTrunk", new Color(0.40f, 0.28f, 0.18f));
        Material leafMaterial = CreateMaterial("TreeLeaf", new Color(0.24f, 0.55f, 0.28f));

        var node = new GameObject(name);
        node.transform.SetParent(parent, false);
        node.transform.localPosition = localPosition;

        switch (kind)
        {
            case GatherKind.Ore:
                // Dark stone with bright crystal shards: nothing else in the world glitters,
                // so a vein is never mistaken for a boulder or a wall.
                CreatePrimitive(PrimitiveType.Sphere, "Stone", node.transform, new Vector3(0f, 0.35f, 0f), new Vector3(1.9f, 0.9f, 1.7f), oreStoneMaterial);
                var shardA = BodyPart(PrimitiveType.Cube, "Crystal", node.transform, new Vector3(-0.35f, 0.85f, 0.1f), new Vector3(0.28f, 1f, 0.28f), oreCrystalMaterial);
                shardA.transform.localRotation = Quaternion.Euler(0f, 20f, -18f);
                var shardB = BodyPart(PrimitiveType.Cube, "Crystal", node.transform, new Vector3(0.3f, 1f, -0.15f), new Vector3(0.24f, 1.3f, 0.24f), oreCrystalMaterial);
                shardB.transform.localRotation = Quaternion.Euler(12f, -30f, 14f);
                var shardC = BodyPart(PrimitiveType.Cube, "Crystal", node.transform, new Vector3(0.05f, 0.75f, 0.45f), new Vector3(0.2f, 0.8f, 0.2f), oreCrystalMaterial);
                shardC.transform.localRotation = Quaternion.Euler(-16f, 5f, 22f);
                break;

            case GatherKind.Herb:
                CreatePrimitive(PrimitiveType.Sphere, "Bush", node.transform, new Vector3(0f, 0.35f, 0f), new Vector3(1.1f, 0.7f, 1.1f), herbMaterial);
                BodyPart(PrimitiveType.Sphere, "Flower", node.transform, new Vector3(-0.3f, 0.68f, 0.15f), new Vector3(0.22f, 0.22f, 0.22f), flowerMaterial);
                BodyPart(PrimitiveType.Sphere, "Flower", node.transform, new Vector3(0.25f, 0.72f, -0.2f), new Vector3(0.2f, 0.2f, 0.2f), flowerMaterial);
                BodyPart(PrimitiveType.Sphere, "Flower", node.transform, new Vector3(0.1f, 0.62f, 0.4f), new Vector3(0.18f, 0.18f, 0.18f), flowerMaterial);
                break;

            default:
                CreatePrimitive(PrimitiveType.Cylinder, "Trunk", node.transform, new Vector3(0f, 1.2f, 0f), new Vector3(0.5f, 1.2f, 0.5f), trunkMaterial);
                CreatePrimitive(PrimitiveType.Sphere, "Leaves", node.transform, new Vector3(0f, 2.9f, 0f), new Vector3(2.6f, 2.2f, 2.6f), leafMaterial);
                BodyPart(PrimitiveType.Sphere, "LeavesSide", node.transform, new Vector3(-0.9f, 2.4f, 0.3f), new Vector3(1.5f, 1.3f, 1.5f), leafMaterial);
                BodyPart(PrimitiveType.Sphere, "LeavesTop", node.transform, new Vector3(0.4f, 3.6f, -0.2f), new Vector3(1.4f, 1.2f, 1.4f), leafMaterial);
                break;
        }

        node.AddComponent<NetworkObject>();
        var gather = node.AddComponent<GatherNode>();
        gather.Kind = kind;
        gather.Yield = kind == GatherKind.Ore ? 1 : 2;
        gather.Exp = kind == GatherKind.Ore ? 5 : 3;
        gather.PromptHeight = kind == GatherKind.Wood ? 3.4f : 1.6f;
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

    /// <summary>
    /// A stone arch with a lit portal sheet inside it. The sheet is colour coded per
    /// destination, so the gate says where it goes before you read the prompt.
    /// </summary>
    static WarpPad BuildWarpPad(Transform parent, string name, Vector3 position, string label, Vector3 destination, string portalName, Color portalColor)
    {
        Material stoneMaterial = CreateMaterial("GateStone", new Color(0.55f, 0.55f, 0.60f));
        Material trimMaterial = CreateMaterial("GateTrim", new Color(0.36f, 0.36f, 0.42f));
        Material portalMaterial = CreateMaterial(portalName, portalColor);

        var pad = new GameObject(name);
        pad.transform.SetParent(parent, false);
        pad.transform.localPosition = position;

        CreateDisc("Base", pad.transform, new Vector3(0f, 0.04f, 0f), 5f, trimMaterial);
        CreateDisc("Inlay", pad.transform, new Vector3(0f, 0.06f, 0f), 3.4f, portalMaterial);

        // Two stepped pillars and a lintel across the top.
        foreach (float side in new[] { -1f, 1f })
        {
            CreatePrimitive(PrimitiveType.Cube, side < 0f ? "PillarLeft" : "PillarRight", pad.transform, new Vector3(side * 2.3f, 2f, 0f), new Vector3(0.7f, 4f, 0.7f), stoneMaterial);
            CreatePrimitive(PrimitiveType.Cube, side < 0f ? "FootLeft" : "FootRight", pad.transform, new Vector3(side * 2.3f, 0.25f, 0f), new Vector3(1.1f, 0.5f, 1.1f), trimMaterial);
            CreatePrimitive(PrimitiveType.Cube, side < 0f ? "CapLeft" : "CapRight", pad.transform, new Vector3(side * 2.3f, 4.1f, 0f), new Vector3(1f, 0.35f, 1f), trimMaterial);
        }

        CreatePrimitive(PrimitiveType.Cube, "Lintel", pad.transform, new Vector3(0f, 4.5f, 0f), new Vector3(5.6f, 0.6f, 0.9f), stoneMaterial);

        // The glowing sheet, and a rune floating above the arch. Neither blocks the way.
        BodyPart(PrimitiveType.Cube, "Portal", pad.transform, new Vector3(0f, 2.2f, 0f), new Vector3(3.6f, 4f, 0.12f), portalMaterial);
        var rune = BodyPart(PrimitiveType.Cube, "Rune", pad.transform, new Vector3(0f, 5.2f, 0f), new Vector3(0.55f, 0.55f, 0.55f), portalMaterial);
        rune.transform.localRotation = Quaternion.Euler(45f, 45f, 0f);

        var warp = pad.AddComponent<WarpPad>();
        warp.Destination = destination;
        warp.Label = label;
        return warp;
    }
    /// <summary>
    /// Third area: a walled dungeon with a guarded corridor and the boss chamber at the end.
    /// Shared, not instanced - the scene is single, so everyone meets the same boss.
    /// There is no ceiling on purpose: the camera does not handle being enclosed.
    /// </summary>
    static void BuildDungeon(GameObject monsterPrefab)
    {
        var dungeon = new GameObject("Dungeon");
        dungeon.transform.position = DungeonCenter;

        Material floorMaterial = CreateMaterial("DungeonFloor", new Color(0.24f, 0.22f, 0.26f));
        Material stoneMaterial = CreateMaterial("DungeonStone", new Color(0.34f, 0.32f, 0.36f));
        Material torchMaterial = CreateMaterial("Torch", new Color(0.98f, 0.65f, 0.25f));
        var floor = CreatePrimitive(PrimitiveType.Plane, "DungeonFloor", dungeon.transform, Vector3.zero, new Vector3(6f, 1f, 6f), floorMaterial);
        floor.isStatic = true;

        // Outer walls, taller than the village so it reads as indoors.
        const float half = 30f;
        const float wallHeight = 8f;
        CreatePrimitive(PrimitiveType.Cube, "OuterNorth", dungeon.transform, new Vector3(0f, wallHeight * 0.5f, half), new Vector3(60f, wallHeight, 1f), stoneMaterial);
        CreatePrimitive(PrimitiveType.Cube, "OuterSouth", dungeon.transform, new Vector3(0f, wallHeight * 0.5f, -half), new Vector3(60f, wallHeight, 1f), stoneMaterial);
        CreatePrimitive(PrimitiveType.Cube, "OuterEast", dungeon.transform, new Vector3(half, wallHeight * 0.5f, 0f), new Vector3(1f, wallHeight, 60f), stoneMaterial);
        CreatePrimitive(PrimitiveType.Cube, "OuterWest", dungeon.transform, new Vector3(-half, wallHeight * 0.5f, 0f), new Vector3(1f, wallHeight, 60f), stoneMaterial);

        // The corridor: two long walls that funnel everyone past the guards into the chamber.
        CreatePrimitive(PrimitiveType.Cube, "CorridorWest", dungeon.transform, new Vector3(-7f, wallHeight * 0.5f, -15f), new Vector3(1f, wallHeight, 30f), stoneMaterial);
        CreatePrimitive(PrimitiveType.Cube, "CorridorEast", dungeon.transform, new Vector3(7f, wallHeight * 0.5f, -15f), new Vector3(1f, wallHeight, 30f), stoneMaterial);

        // Chamber walls, open towards the corridor mouth.
        CreatePrimitive(PrimitiveType.Cube, "ChamberWest", dungeon.transform, new Vector3(-18f, wallHeight * 0.5f, 8f), new Vector3(1f, wallHeight, 34f), stoneMaterial);
        CreatePrimitive(PrimitiveType.Cube, "ChamberEast", dungeon.transform, new Vector3(18f, wallHeight * 0.5f, 8f), new Vector3(1f, wallHeight, 34f), stoneMaterial);

        for (int i = 0; i < 6; i++)
        {
            float z = -26f + i * 5f;
            CreatePrimitive(PrimitiveType.Cube, $"TorchWest{i}", dungeon.transform, new Vector3(-6.2f, 3.2f, z), new Vector3(0.3f, 0.6f, 0.3f), torchMaterial);
            CreatePrimitive(PrimitiveType.Cube, $"TorchEast{i}", dungeon.transform, new Vector3(6.2f, 3.2f, z), new Vector3(0.3f, 0.6f, 0.3f), torchMaterial);
        }

        // Pedestal the boss stands on, so the chamber has an obvious centre.
        CreatePrimitive(PrimitiveType.Cube, "Pedestal", dungeon.transform, new Vector3(0f, 0.15f, 14f), new Vector3(12f, 0.3f, 12f), stoneMaterial);

        var guards = new GameObject("DungeonGuards");
        guards.transform.SetParent(dungeon.transform, false);
        guards.transform.localPosition = new Vector3(0f, 0f, -14f);
        var guardSpawner = guards.AddComponent<MonsterSpawner>();
        guardSpawner.MonsterPrefab = monsterPrefab;
        guardSpawner.Count = 6;
        guardSpawner.Radius = 5f;
        guardSpawner.LevelBonus = 3;

        // Two chests flanking the pedestal, shut until the boss goes down.
        BuildChest(dungeon.transform, "ChestLeft", new Vector3(-4.5f, 0.3f, 14f));
        BuildChest(dungeon.transform, "ChestRight", new Vector3(4.5f, 0.3f, 14f));

        var bossPoint = new GameObject("BossSpawner");
        bossPoint.transform.SetParent(dungeon.transform, false);
        bossPoint.transform.localPosition = new Vector3(0f, 0f, 14f);
        var bossSpawner = bossPoint.AddComponent<MonsterSpawner>();
        bossSpawner.MonsterPrefab = monsterPrefab;
        bossSpawner.Boss = true;

        BuildWarpPad(dungeon.transform, "WarpPadDungeonExit", DungeonPad - DungeonCenter, "Village", VillageDungeonArrival, "PortalVillage", new Color(0.30f, 0.80f, 0.85f));
    }


    /// <summary>Second area of the scene: open ground, walls, monsters and the way home.</summary>
    static void BuildHuntingField(GameObject monsterPrefab)
    {
        var field = new GameObject("HuntingField");
        field.transform.position = FieldCenter;

        Material fieldMaterial = CreateMaterial("FieldGround", new Color(0.28f, 0.38f, 0.26f));
        Material wallMaterial = CreateMaterial("Wall", new Color(0.30f, 0.32f, 0.38f));

        var ground = CreatePrimitive(PrimitiveType.Plane, "FieldGround", field.transform, Vector3.zero, new Vector3(6f, 1f, 6f), fieldMaterial);
        ground.isStatic = true;

        const float half = 30f;
        CreatePrimitive(PrimitiveType.Cube, "FieldWallNorth", field.transform, new Vector3(0f, 2f, half), new Vector3(60f, 4f, 1f), wallMaterial);
        CreatePrimitive(PrimitiveType.Cube, "FieldWallSouth", field.transform, new Vector3(0f, 2f, -half), new Vector3(60f, 4f, 1f), wallMaterial);
        CreatePrimitive(PrimitiveType.Cube, "FieldWallEast", field.transform, new Vector3(half, 2f, 0f), new Vector3(1f, 4f, 60f), wallMaterial);
        CreatePrimitive(PrimitiveType.Cube, "FieldWallWest", field.transform, new Vector3(-half, 2f, 0f), new Vector3(1f, 4f, 60f), wallMaterial);

        // Field scenery is all natural shapes: round boulders, bare dead trees and grass
        // tufts. Nothing here is a box, so it never gets confused with village masonry.
        Material bouldMaterial = CreateMaterial("Boulder", new Color(0.44f, 0.46f, 0.42f));
        Material deadWoodMaterial = CreateMaterial("DeadWood", new Color(0.34f, 0.27f, 0.21f));
        Material tuftMaterial = CreateMaterial("GrassTuft", new Color(0.42f, 0.58f, 0.28f));

        var boulders = new (Vector3 position, float size)[]
        {
            (new Vector3(-14f, 0f, 9f), 3f),
            (new Vector3(11f, 0f, 14f), 4f),
            (new Vector3(18f, 0f, -8f), 2.5f),
            (new Vector3(-9f, 0f, -15f), 3.5f),
            (new Vector3(4f, 0f, 20f), 2f),
        };
        for (int i = 0; i < boulders.Length; i++)
        {
            float size = boulders[i].size;
            var boulder = CreatePrimitive(PrimitiveType.Sphere, $"Boulder{i}", field.transform,
                new Vector3(boulders[i].position.x, size * 0.3f, boulders[i].position.z),
                new Vector3(size, size * 0.75f, size * 0.9f),
                bouldMaterial);
            boulder.transform.localRotation = Quaternion.Euler(0f, i * 37f, 8f);
            BodyPart(PrimitiveType.Sphere, $"BoulderChip{i}", field.transform,
                new Vector3(boulders[i].position.x + size * 0.55f, size * 0.18f, boulders[i].position.z - size * 0.4f),
                new Vector3(size * 0.45f, size * 0.35f, size * 0.4f), bouldMaterial);
        }

        var deadTrees = new[]
        {
            new Vector3(-20f, 0f, 2f), new Vector3(7f, 0f, -22f), new Vector3(24f, 0f, 6f),
        };
        for (int i = 0; i < deadTrees.Length; i++)
        {
            var tree = new GameObject($"DeadTree{i}");
            tree.transform.SetParent(field.transform, false);
            tree.transform.localPosition = deadTrees[i];
            CreatePrimitive(PrimitiveType.Cylinder, "Trunk", tree.transform, new Vector3(0f, 2f, 0f), new Vector3(0.45f, 2f, 0.45f), deadWoodMaterial);
            var branchLeft = BodyPart(PrimitiveType.Cylinder, "BranchLeft", tree.transform, new Vector3(-0.7f, 3f, 0f), new Vector3(0.2f, 0.9f, 0.2f), deadWoodMaterial);
            branchLeft.transform.localRotation = Quaternion.Euler(0f, 0f, 55f);
            var branchRight = BodyPart(PrimitiveType.Cylinder, "BranchRight", tree.transform, new Vector3(0.7f, 3.4f, 0.1f), new Vector3(0.18f, 0.8f, 0.18f), deadWoodMaterial);
            branchRight.transform.localRotation = Quaternion.Euler(10f, 0f, -50f);
        }

        for (int i = 0; i < 14; i++)
        {
            float angle = i * 137f * Mathf.Deg2Rad;
            float radius = 6f + (i % 5) * 4f;
            var tuft = BodyPart(PrimitiveType.Cube, $"Grass{i}", field.transform,
                new Vector3(Mathf.Cos(angle) * radius, 0.25f, Mathf.Sin(angle) * radius),
                new Vector3(0.5f, 0.5f, 0.5f), tuftMaterial);
            tuft.transform.localRotation = Quaternion.Euler(0f, i * 23f, 12f);
        }

        BuildWarpPad(field.transform, "WarpPadField", FieldPad - FieldCenter, "Village", VillageArrival, "PortalVillage", new Color(0.30f, 0.80f, 0.85f));

        var oreSpots = new[]
        {
            new Vector3(-12f, 0f, 12f), new Vector3(14f, 0f, -6f),
            new Vector3(-4f, 0f, -18f), new Vector3(20f, 0f, 16f),
        };
        for (int i = 0; i < oreSpots.Length; i++)
        {
            BuildGatherNode(field.transform, "FieldOre" + i, oreSpots[i], GatherKind.Ore);
        }

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
        Material boneMaterial = CreateMaterial("MonsterBone", new Color(0.90f, 0.88f, 0.80f));
        Material clawMaterial = CreateMaterial("MonsterClaw", new Color(0.20f, 0.18f, 0.20f));

        // One body per kind, all built into the same prefab. Monster shows only the one
        // that matches its kind, which keeps a single network prefab.
        var bodies = new[]
        {
            BuildSlimeBody(root.transform, bodyMaterial, eyeMaterial),
            BuildGoblinBody(root.transform, bodyMaterial, eyeMaterial, clawMaterial),
            BuildOrcBody(root.transform, bodyMaterial, eyeMaterial, boneMaterial),
            BuildOgreBody(root.transform, bodyMaterial, eyeMaterial, boneMaterial),
        };

        // A controller rather than a plain collider: the server moves monsters with it, so
        // they collide with walls instead of sliding through them.
        var controller = root.AddComponent<CharacterController>();
        controller.height = 2.2f;
        controller.radius = 0.5f;
        controller.center = new Vector3(0f, 1.1f, 0f);
        controller.slopeLimit = 55f;
        controller.stepOffset = 0.5f;

        root.AddComponent<NetworkObject>();

        var networkTransform = root.AddComponent<NetworkTransform>();
        networkTransform.Interpolate = true;
        networkTransform.SyncScaleX = false;
        networkTransform.SyncScaleY = false;
        networkTransform.SyncScaleZ = false;

        Material ringMaterial = CreateMaterial("SlamRing", new Color(1f, 0.62f, 0.30f));
        var slamRing = CreateDisc("SlamRing", root.transform, new Vector3(0f, 0.06f, 0f), 1f, ringMaterial);
        slamRing.SetActive(false);

        var monster = root.AddComponent<Monster>();
        monster.Bodies = bodies;
        monster.SlamRing = slamRing;

        for (int i = 1; i < bodies.Length; i++)
        {
            bodies[i].SetActive(false);
        }

        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, MonsterPrefabPath);
        Object.DestroyImmediate(root);
        return prefab;
    }

    /// <summary>Blob with a wobbling top and two eyes: low, wide, no limbs.</summary>
    static GameObject BuildSlimeBody(Transform parent, Material bodyMaterial, Material eyeMaterial)
    {
        var body = new GameObject("SlimeBody");
        body.transform.SetParent(parent, false);

        BodyPart(PrimitiveType.Sphere, "Blob", body.transform, new Vector3(0f, 0.5f, 0f), new Vector3(1.5f, 0.95f, 1.4f), bodyMaterial);
        BodyPart(PrimitiveType.Sphere, "Cap", body.transform, new Vector3(0f, 0.92f, -0.05f), new Vector3(0.85f, 0.5f, 0.8f), bodyMaterial);
        BodyPart(PrimitiveType.Sphere, "EyeLeft", body.transform, new Vector3(-0.28f, 0.68f, 0.6f), new Vector3(0.22f, 0.22f, 0.12f), eyeMaterial);
        BodyPart(PrimitiveType.Sphere, "EyeRight", body.transform, new Vector3(0.28f, 0.68f, 0.6f), new Vector3(0.22f, 0.22f, 0.12f), eyeMaterial);
        return body;
    }

    /// <summary>Small hunched humanoid with pointed ears and claws.</summary>
    static GameObject BuildGoblinBody(Transform parent, Material bodyMaterial, Material eyeMaterial, Material clawMaterial)
    {
        var body = new GameObject("GoblinBody");
        body.transform.SetParent(parent, false);

        BodyPart(PrimitiveType.Cube, "Torso", body.transform, new Vector3(0f, 0.85f, 0f), new Vector3(0.6f, 0.7f, 0.45f), bodyMaterial);
        BodyPart(PrimitiveType.Sphere, "Head", body.transform, new Vector3(0f, 1.4f, 0.05f), new Vector3(0.5f, 0.5f, 0.5f), bodyMaterial);
        BodyPart(PrimitiveType.Cube, "EarLeft", body.transform, new Vector3(-0.34f, 1.5f, 0f), new Vector3(0.3f, 0.12f, 0.1f), bodyMaterial);
        BodyPart(PrimitiveType.Cube, "EarRight", body.transform, new Vector3(0.34f, 1.5f, 0f), new Vector3(0.3f, 0.12f, 0.1f), bodyMaterial);
        BodyPart(PrimitiveType.Sphere, "EyeLeft", body.transform, new Vector3(-0.14f, 1.45f, 0.24f), new Vector3(0.13f, 0.13f, 0.08f), eyeMaterial);
        BodyPart(PrimitiveType.Sphere, "EyeRight", body.transform, new Vector3(0.14f, 1.45f, 0.24f), new Vector3(0.13f, 0.13f, 0.08f), eyeMaterial);
        BodyPart(PrimitiveType.Cube, "ArmLeft", body.transform, new Vector3(-0.42f, 0.85f, 0.08f), new Vector3(0.16f, 0.62f, 0.18f), bodyMaterial);
        BodyPart(PrimitiveType.Cube, "ArmRight", body.transform, new Vector3(0.42f, 0.85f, 0.08f), new Vector3(0.16f, 0.62f, 0.18f), bodyMaterial);
        BodyPart(PrimitiveType.Cube, "ClawLeft", body.transform, new Vector3(-0.42f, 0.5f, 0.14f), new Vector3(0.18f, 0.16f, 0.24f), clawMaterial);
        BodyPart(PrimitiveType.Cube, "ClawRight", body.transform, new Vector3(0.42f, 0.5f, 0.14f), new Vector3(0.18f, 0.16f, 0.24f), clawMaterial);
        BodyPart(PrimitiveType.Cube, "LegLeft", body.transform, new Vector3(-0.17f, 0.25f, 0f), new Vector3(0.2f, 0.5f, 0.22f), bodyMaterial);
        BodyPart(PrimitiveType.Cube, "LegRight", body.transform, new Vector3(0.17f, 0.25f, 0f), new Vector3(0.2f, 0.5f, 0.22f), bodyMaterial);
        return body;
    }

    /// <summary>Heavy brute: broad shoulders, thick arms, tusks.</summary>
    static GameObject BuildOrcBody(Transform parent, Material bodyMaterial, Material eyeMaterial, Material boneMaterial)
    {
        var body = new GameObject("OrcBody");
        body.transform.SetParent(parent, false);

        BodyPart(PrimitiveType.Cube, "Torso", body.transform, new Vector3(0f, 1.15f, 0f), new Vector3(1.05f, 0.95f, 0.65f), bodyMaterial);
        BodyPart(PrimitiveType.Cube, "Shoulders", body.transform, new Vector3(0f, 1.6f, 0f), new Vector3(1.5f, 0.32f, 0.7f), bodyMaterial);
        BodyPart(PrimitiveType.Sphere, "Head", body.transform, new Vector3(0f, 1.95f, 0.08f), new Vector3(0.62f, 0.6f, 0.62f), bodyMaterial);
        BodyPart(PrimitiveType.Cube, "TuskLeft", body.transform, new Vector3(-0.16f, 1.82f, 0.3f), new Vector3(0.09f, 0.26f, 0.09f), boneMaterial);
        BodyPart(PrimitiveType.Cube, "TuskRight", body.transform, new Vector3(0.16f, 1.82f, 0.3f), new Vector3(0.09f, 0.26f, 0.09f), boneMaterial);
        BodyPart(PrimitiveType.Sphere, "EyeLeft", body.transform, new Vector3(-0.17f, 2.02f, 0.3f), new Vector3(0.14f, 0.14f, 0.08f), eyeMaterial);
        BodyPart(PrimitiveType.Sphere, "EyeRight", body.transform, new Vector3(0.17f, 2.02f, 0.3f), new Vector3(0.14f, 0.14f, 0.08f), eyeMaterial);
        BodyPart(PrimitiveType.Cube, "ArmLeft", body.transform, new Vector3(-0.78f, 1.15f, 0.05f), new Vector3(0.3f, 0.95f, 0.32f), bodyMaterial);
        BodyPart(PrimitiveType.Cube, "ArmRight", body.transform, new Vector3(0.78f, 1.15f, 0.05f), new Vector3(0.3f, 0.95f, 0.32f), bodyMaterial);
        BodyPart(PrimitiveType.Cube, "LegLeft", body.transform, new Vector3(-0.3f, 0.35f, 0f), new Vector3(0.34f, 0.7f, 0.36f), bodyMaterial);
        BodyPart(PrimitiveType.Cube, "LegRight", body.transform, new Vector3(0.3f, 0.35f, 0f), new Vector3(0.34f, 0.7f, 0.36f), bodyMaterial);
        return body;
    }

    /// <summary>The dungeon boss: everything bigger, plus horns and a hunched back.</summary>
    static GameObject BuildOgreBody(Transform parent, Material bodyMaterial, Material eyeMaterial, Material boneMaterial)
    {
        var body = new GameObject("OgreBody");
        body.transform.SetParent(parent, false);

        BodyPart(PrimitiveType.Cube, "Torso", body.transform, new Vector3(0f, 1.4f, 0f), new Vector3(1.5f, 1.3f, 0.9f), bodyMaterial);
        BodyPart(PrimitiveType.Sphere, "Hump", body.transform, new Vector3(0f, 2.05f, -0.3f), new Vector3(1.1f, 0.7f, 0.8f), bodyMaterial);
        BodyPart(PrimitiveType.Sphere, "Head", body.transform, new Vector3(0f, 2.45f, 0.12f), new Vector3(0.8f, 0.78f, 0.8f), bodyMaterial);
        BodyPart(PrimitiveType.Cube, "HornLeft", body.transform, new Vector3(-0.34f, 2.85f, 0.05f), new Vector3(0.14f, 0.5f, 0.14f), boneMaterial);
        BodyPart(PrimitiveType.Cube, "HornRight", body.transform, new Vector3(0.34f, 2.85f, 0.05f), new Vector3(0.14f, 0.5f, 0.14f), boneMaterial);
        BodyPart(PrimitiveType.Sphere, "EyeLeft", body.transform, new Vector3(-0.22f, 2.52f, 0.38f), new Vector3(0.18f, 0.18f, 0.1f), eyeMaterial);
        BodyPart(PrimitiveType.Sphere, "EyeRight", body.transform, new Vector3(0.22f, 2.52f, 0.38f), new Vector3(0.18f, 0.18f, 0.1f), eyeMaterial);
        BodyPart(PrimitiveType.Cube, "ArmLeft", body.transform, new Vector3(-1.05f, 1.4f, 0.05f), new Vector3(0.42f, 1.3f, 0.44f), bodyMaterial);
        BodyPart(PrimitiveType.Cube, "ArmRight", body.transform, new Vector3(1.05f, 1.4f, 0.05f), new Vector3(0.42f, 1.3f, 0.44f), bodyMaterial);
        BodyPart(PrimitiveType.Cube, "FistLeft", body.transform, new Vector3(-1.05f, 0.72f, 0.08f), new Vector3(0.55f, 0.45f, 0.55f), bodyMaterial);
        BodyPart(PrimitiveType.Cube, "FistRight", body.transform, new Vector3(1.05f, 0.72f, 0.08f), new Vector3(0.55f, 0.45f, 0.55f), bodyMaterial);
        BodyPart(PrimitiveType.Cube, "LegLeft", body.transform, new Vector3(-0.42f, 0.4f, 0f), new Vector3(0.46f, 0.8f, 0.48f), bodyMaterial);
        BodyPart(PrimitiveType.Cube, "LegRight", body.transform, new Vector3(0.42f, 0.4f, 0f), new Vector3(0.46f, 0.8f, 0.48f), bodyMaterial);
        return body;
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
        root.AddComponent<PlayerBuffs>();
        root.AddComponent<PlayerInventory>();
        root.AddComponent<PlayerQuests>();
        root.AddComponent<PlayerEmotes>();

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
        // Two eyes rather than one visor strip, so the face reads as a face.
        BodyPart(PrimitiveType.Cube, "EyeLeft", rig.transform, new Vector3(-0.11f, 1.77f, 0.22f), new Vector3(0.09f, 0.1f, 0.03f), faceMaterial);
        BodyPart(PrimitiveType.Cube, "EyeRight", rig.transform, new Vector3(0.11f, 1.77f, 0.22f), new Vector3(0.09f, 0.1f, 0.03f), faceMaterial);

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

        Material trimMaterial = CreateMaterial("SwordTrim", new Color(0.80f, 0.66f, 0.28f));
        Material fullerMaterial = CreateMaterial("SwordFuller", new Color(0.52f, 0.55f, 0.62f));

        // Hilt: pommel, wrapped grip, crossguard with turned tips.
        BodyPart(PrimitiveType.Cube, "Pommel", sword.transform, new Vector3(0f, 0.07f, 0f), new Vector3(0.11f, 0.09f, 0.11f), trimMaterial);
        BodyPart(PrimitiveType.Cube, "Grip", sword.transform, new Vector3(0f, -0.05f, 0f), new Vector3(0.07f, 0.2f, 0.07f), gripMaterial);
        BodyPart(PrimitiveType.Cube, "WrapUpper", sword.transform, new Vector3(0f, -0.01f, 0f), new Vector3(0.085f, 0.02f, 0.085f), trimMaterial);
        BodyPart(PrimitiveType.Cube, "WrapLower", sword.transform, new Vector3(0f, -0.09f, 0f), new Vector3(0.085f, 0.02f, 0.085f), trimMaterial);

        BodyPart(PrimitiveType.Cube, "Guard", sword.transform, new Vector3(0f, -0.17f, 0f), new Vector3(0.3f, 0.06f, 0.1f), trimMaterial);
        foreach (float side in new[] { -1f, 1f })
        {
            var tip = BodyPart(PrimitiveType.Cube, side < 0f ? "GuardTipLeft" : "GuardTipRight", sword.transform,
                new Vector3(side * 0.17f, -0.145f, 0f), new Vector3(0.07f, 0.09f, 0.09f), trimMaterial);
            tip.transform.localRotation = Quaternion.Euler(0f, 0f, side * 28f);
        }

        // Blade: a wide upper half, a narrower lower half, a fuller down the middle, a point.
        BodyPart(PrimitiveType.Cube, "Ricasso", sword.transform, new Vector3(0f, -0.23f, 0f), new Vector3(0.1f, 0.07f, 0.05f), bladeMaterial);
        BodyPart(PrimitiveType.Cube, "BladeUpper", sword.transform, new Vector3(0f, -0.42f, 0f), new Vector3(0.115f, 0.34f, 0.042f), bladeMaterial);
        BodyPart(PrimitiveType.Cube, "BladeLower", sword.transform, new Vector3(0f, -0.68f, 0f), new Vector3(0.085f, 0.22f, 0.036f), bladeMaterial);
        BodyPart(PrimitiveType.Cube, "Fuller", sword.transform, new Vector3(0f, -0.5f, 0.024f), new Vector3(0.032f, 0.5f, 0.012f), fullerMaterial);

        var point = BodyPart(PrimitiveType.Cube, "Point", sword.transform, new Vector3(0f, -0.81f, 0f), new Vector3(0.062f, 0.062f, 0.036f), bladeMaterial);
        point.transform.localRotation = Quaternion.Euler(0f, 0f, 45f);
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

        managerObject.AddComponent<SaveSystem>();

        var chatObject = new GameObject("ChatSystem");
        chatObject.AddComponent<NetworkObject>();
        chatObject.AddComponent<ChatSystem>();

        var tradeObject = new GameObject("TradeSystem");
        tradeObject.AddComponent<NetworkObject>();
        tradeObject.AddComponent<TradeSystem>();

        var partyObject = new GameObject("PartySystem");
        partyObject.AddComponent<NetworkObject>();
        partyObject.AddComponent<PartySystem>();
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
