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
    /// <summary>Half the width of one area, which is what its walls stand on.</summary>
    const float AreaHalfWidth = 30f;

    const string Root = "Assets/Metaverse";
    const string ScenePath = Root + "/Scenes/Metaverse.unity";
    const string PrefabPath = Root + "/Prefabs/PlayerAvatar.prefab";
    const string MonsterPrefabPath = Root + "/Prefabs/Monster.prefab";
    const string NetworkPrefabsListPath = "Assets/DefaultNetworkPrefabs.asset";
    const string MaterialFolder = Root + "/Materials";

    // Hunting grounds and dungeons are further areas of the same scene, far enough away
    // that the village never sees them. Warp pads are the only way across.
    static readonly Vector3 VillagePad = new(10f, 0f, -12f);
    static readonly Vector3 VillageArrival = new(13.5f, 1f, -12f);

    /// <summary>A hunting ground: where it stands, how hard it hits, and its palette.</summary>
    class FieldArea
    {
        public string Name;

        /// <summary>Suffix for generated objects and materials; empty for the first field.</summary>
        public string Key = "";

        public Vector3 Center;

        /// <summary>Levels added on top of the players', so a themed ground is a step up.</summary>
        public int Bonus;

        /// <summary>Index into Monster.Rosters: which creatures live here.</summary>
        public int Theme;

        /// <summary>Level needed at the gate that leads here.</summary>
        public int Required;

        public Color Ground,
            Wall,
            Rock,
            Wood,
            Tuft,
            Prop,
            Portal;

        public Vector3 Arrival => Center + new Vector3(0f, 1f, -21.5f);
    }

    /// <summary>The same, for the dungeons: guards, a boss and a chest haul.</summary>
    class DungeonArea
    {
        public string Name;
        public string Key = "";
        public Vector3 Center;
        public int GuardBonus;
        public int BossBonus;
        public int Theme;
        public int Required;
        public int ChestGold,
            ChestExp,
            ChestOre;
        public Color Floor,
            Stone,
            Torch,
            Portal;

        public Vector3 Arrival => Center + new Vector3(0f, 1f, -22.5f);
    }

    // A three by three grid with the village in the middle, 120 apart: the areas are 60
    // across, so that is a clear 60 between any two of them, and every trip is the same
    // distance from home.
    static readonly FieldArea[] FieldAreas =
    {
        new FieldArea
        {
            Name = "초원 사냥터",
            Center = new Vector3(120f, 0f, 0f),
            Ground = new Color(0.28f, 0.38f, 0.26f),
            Wall = new Color(0.30f, 0.32f, 0.38f),
            Rock = new Color(0.44f, 0.46f, 0.42f),
            Wood = new Color(0.34f, 0.27f, 0.21f),
            Tuft = new Color(0.42f, 0.58f, 0.28f),
            Prop = new Color(0.44f, 0.46f, 0.42f),
            Portal = new Color(0.98f, 0.55f, 0.20f),
        },
        new FieldArea
        {
            Name = "서리 설원",
            Key = "Frost",
            Center = new Vector3(120f, 0f, 120f),
            Bonus = 4,
            Theme = 1,
            Required = 10,
            Ground = new Color(0.78f, 0.84f, 0.90f),
            Wall = new Color(0.58f, 0.66f, 0.74f),
            Rock = new Color(0.62f, 0.70f, 0.78f),
            Wood = new Color(0.42f, 0.46f, 0.54f),
            Tuft = new Color(0.90f, 0.95f, 1f),
            Prop = new Color(0.66f, 0.86f, 0.96f),
            Portal = new Color(0.55f, 0.85f, 1f),
        },
        new FieldArea
        {
            Name = "용암 지대",
            Key = "Ember",
            Center = new Vector3(120f, 0f, -120f),
            Bonus = 8,
            Theme = 2,
            Required = 20,
            Ground = new Color(0.22f, 0.18f, 0.17f),
            Wall = new Color(0.26f, 0.21f, 0.20f),
            Rock = new Color(0.31f, 0.27f, 0.26f),
            Wood = new Color(0.16f, 0.14f, 0.14f),
            Tuft = new Color(0.85f, 0.35f, 0.15f),
            Prop = new Color(1f, 0.45f, 0.12f),
            Portal = new Color(1f, 0.45f, 0.15f),
        },
    };

    static readonly DungeonArea[] DungeonAreas =
    {
        new DungeonArea
        {
            Name = "폐허 던전",
            Center = new Vector3(-120f, 0f, 0f),
            GuardBonus = 3,
            Required = 5,
            ChestGold = 90,
            ChestExp = 40,
            ChestOre = 2,
            Floor = new Color(0.24f, 0.22f, 0.26f),
            Stone = new Color(0.34f, 0.32f, 0.36f),
            Torch = new Color(0.98f, 0.65f, 0.25f),
            Portal = new Color(0.65f, 0.30f, 0.90f),
        },
        new DungeonArea
        {
            Name = "서리 묘지",
            Key = "FrostCrypt",
            Center = new Vector3(-120f, 0f, 120f),
            GuardBonus = 7,
            BossBonus = 4,
            Theme = 1,
            Required = 15,
            ChestGold = 180,
            ChestExp = 90,
            ChestOre = 3,
            Floor = new Color(0.36f, 0.42f, 0.50f),
            Stone = new Color(0.48f, 0.56f, 0.66f),
            Torch = new Color(0.55f, 0.85f, 1f),
            Portal = new Color(0.55f, 0.85f, 1f),
        },
        new DungeonArea
        {
            Name = "용암 심연",
            Key = "EmberDepths",
            Center = new Vector3(-120f, 0f, -120f),
            GuardBonus = 11,
            BossBonus = 8,
            Theme = 2,
            Required = 25,
            ChestGold = 320,
            ChestExp = 160,
            ChestOre = 5,
            Floor = new Color(0.18f, 0.14f, 0.14f),
            Stone = new Color(0.28f, 0.22f, 0.22f),
            Torch = new Color(1f, 0.42f, 0.12f),
            Portal = new Color(1f, 0.42f, 0.12f),
        },
    };

    // Fourth area: the duelling arena.
    static readonly Vector3 ArenaCentre = new(0f, 0f, 120f);
    const float ArenaRadius = 22f;
    static readonly Vector3 CornerA = new(0f, 1f, 106f);
    static readonly Vector3 CornerB = new(0f, 1f, 134f);
    static readonly Vector3 VillageArenaPad = new(0f, 0f, -13f);
    static readonly Vector3 ArenaPad = new(0f, 0f, 96f);
    static readonly Vector3 VillageArenaArrival = new(0f, 1f, -9.5f);
    static readonly Vector3 ArenaArrival = new(0f, 1f, 99.5f);

    // The dungeon gate stands on the far side of the plaza from the hunting gate.
    static readonly Vector3 VillageDungeonPad = new(-10f, 0f, -12f);
    static readonly Vector3 VillageDungeonArrival = new(-13.5f, 1f, -12f);

    // The lake, and the gate to it: tucked between the two houses behind the shopkeeper.
    static readonly Vector3 LakeCentre = new(0f, 0f, -120f);
    static readonly Vector3 VillageLakePad = new(-17f, 0f, 4f);

    // Beside the arch rather than in front of it: the gate faces along the gap now, and the
    // tree behind it is something to walk into.
    static readonly Vector3 VillageLakeArrival = new(-13.5f, 1f, 2f);
    static readonly Vector3 LakeArrival = new(0f, 1f, -142f);

    [MenuItem("Tools/Metaverse/Build World Scene")]
    public static void Build()
    {
        EnsureFolders();

        var scene = EditorSceneManager.NewScene(
            NewSceneSetup.DefaultGameObjects,
            NewSceneMode.Single
        );

        GameObject monsterPrefab = BuildMonsterPrefab();
        BuildWorld();

        foreach (var area in FieldAreas)
        {
            BuildHuntingField(monsterPrefab, area);
        }

        foreach (var area in DungeonAreas)
        {
            BuildDungeon(monsterPrefab, area);
        }

        BuildArena();
        BuildLake();
        GameObject playerPrefab = BuildPlayerPrefab();
        BuildNetworking(playerPrefab, monsterPrefab);
        SetupCamera();
        SetupLight();

        // Scenery never moves, so Unity is allowed to fold it into a handful of draw calls.
        // Anything spawned or animated carries a NetworkObject and is left alone.
        foreach (var root in scene.GetRootGameObjects())
        {
            foreach (var renderer in root.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (renderer.GetComponentInParent<NetworkObject>() == null)
                {
                    renderer.gameObject.isStatic = true;
                }
            }
        }

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
        var onValidate = typeof(NetworkObject).GetMethod(
            "OnValidate",
            BindingFlags.Instance | BindingFlags.NonPublic
        );
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

        foreach (
            var sceneObject in Object.FindObjectsByType<NetworkObject>(FindObjectsSortMode.None)
        )
        {
            onValidate.Invoke(sceneObject, null);
            EditorUtility.SetDirty(sceneObject);
            uint hash = new SerializedObject(sceneObject)
                .FindProperty("GlobalObjectIdHash")
                .uintValue;
            Debug.Log($"[Metaverse] scene object {sceneObject.name} id={hash}");
        }

        AssetDatabase.SaveAssets();
    }

    static void EnsureFolders()
    {
        foreach (
            string folder in new[] { Root, Root + "/Scenes", Root + "/Prefabs", MaterialFolder }
        )
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

        var ground = CreatePrimitive(
            PrimitiveType.Plane,
            "Ground",
            world.transform,
            Vector3.zero,
            new Vector3(6f, 1f, 6f),
            groundMaterial
        );
        ground.isStatic = true;

        CreateDisc("Plaza", world.transform, new Vector3(0f, 0.02f, 0f), 20f, plazaMaterial);

        // Boundary walls so nobody walks off the world.
        BuildBounds(world.transform, "", wallMaterial);

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

        BuildMonument(world.transform, accentMaterial, buildingMaterial);

        BuildShopNpc(world.transform);
        // Two arches for six places: each one asks where to go, so the plaza does not fill
        // up with gates as areas are added.
        var fieldPad = BuildWarpPad(
            world.transform,
            "WarpPadVillage",
            VillagePad,
            "사냥터",
            FieldAreas[0].Arrival,
            "PortalField",
            FieldAreas[0].Portal
        );
        fieldPad.Choices = System.Array.ConvertAll(
            FieldAreas,
            area => new WarpPad.Choice
            {
                Name = area.Name,
                Destination = area.Arrival,
                RequiredLevel = area.Required,
            }
        );

        var dungeonPad = BuildWarpPad(
            world.transform,
            "WarpPadDungeon",
            VillageDungeonPad,
            "던전",
            DungeonAreas[0].Arrival,
            "PortalDungeon",
            DungeonAreas[0].Portal
        );
        dungeonPad.Choices = System.Array.ConvertAll(
            DungeonAreas,
            area => new WarpPad.Choice
            {
                Name = area.Name,
                Destination = area.Arrival,
                RequiredLevel = area.Required,
            }
        );

        BuildWarpPad(
            world.transform,
            "WarpPadArena",
            VillageArenaPad,
            "아레나",
            ArenaArrival,
            "PortalArena",
            new Color(0.90f, 0.30f, 0.35f)
        );
        var lakePad = BuildWarpPad(
            world.transform,
            "WarpPadLake",
            VillageLakePad,
            "호수",
            LakeArrival,
            "PortalLake",
            new Color(0.25f, 0.62f, 0.95f)
        );
        lakePad.transform.localRotation = Quaternion.Euler(0f, 90f, 0f);

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

        CreatePrimitive(
            PrimitiveType.Cube,
            "Walls",
            house.transform,
            new Vector3(0f, size.y * 0.5f, 0f),
            size,
            wallMaterial
        );

        // Roof: two overhanging slabs leaning against each other.
        float roofY = size.y + 0.35f;
        CreatePrimitive(
            PrimitiveType.Cube,
            "RoofLeft",
            house.transform,
            new Vector3(-size.x * 0.22f, roofY, 0f),
            new Vector3(size.x * 0.62f, 0.3f, size.z + 1.2f),
            roofMaterial,
            Quaternion.Euler(0f, 0f, 22f)
        );
        CreatePrimitive(
            PrimitiveType.Cube,
            "RoofRight",
            house.transform,
            new Vector3(size.x * 0.22f, roofY, 0f),
            new Vector3(size.x * 0.62f, 0.3f, size.z + 1.2f),
            roofMaterial,
            Quaternion.Euler(0f, 0f, -22f)
        );

        // Corner beams, so the walls do not read as one flat block.
        float halfX = size.x * 0.5f;
        float halfZ = size.z * 0.5f;
        foreach (
            var corner in new[]
            {
                new Vector2(-halfX, -halfZ),
                new Vector2(halfX, -halfZ),
                new Vector2(-halfX, halfZ),
                new Vector2(halfX, halfZ),
            }
        )
        {
            CreatePrimitive(
                PrimitiveType.Cube,
                "Beam",
                house.transform,
                new Vector3(corner.x, size.y * 0.5f, corner.y),
                new Vector3(0.35f, size.y, 0.35f),
                beamMaterial
            );
        }

        // Door and windows on the plaza facing side.
        float front = -halfZ - 0.06f;
        BodyPart(
            PrimitiveType.Cube,
            "Door",
            house.transform,
            new Vector3(0f, 1.1f, front),
            new Vector3(1.3f, 2.2f, 0.15f),
            beamMaterial
        );
        BodyPart(
            PrimitiveType.Cube,
            "WindowLeft",
            house.transform,
            new Vector3(-size.x * 0.28f, size.y * 0.6f, front),
            new Vector3(1.1f, 1.1f, 0.15f),
            windowMaterial
        );
        BodyPart(
            PrimitiveType.Cube,
            "WindowRight",
            house.transform,
            new Vector3(size.x * 0.28f, size.y * 0.6f, front),
            new Vector3(1.1f, 1.1f, 0.15f),
            windowMaterial
        );
    }

    /// <summary>Obelisk on a stepped base, the landmark at the centre of the plaza.</summary>
    static void BuildMonument(Transform parent, Material accentMaterial, Material stoneMaterial)
    {
        var monument = new GameObject("Monument");
        monument.transform.SetParent(parent, false);

        CreatePrimitive(
            PrimitiveType.Cube,
            "BaseLower",
            monument.transform,
            new Vector3(0f, 0.25f, 0f),
            new Vector3(4f, 0.5f, 4f),
            stoneMaterial
        );
        CreatePrimitive(
            PrimitiveType.Cube,
            "BaseUpper",
            monument.transform,
            new Vector3(0f, 0.75f, 0f),
            new Vector3(2.8f, 0.5f, 2.8f),
            stoneMaterial
        );
        CreatePrimitive(
            PrimitiveType.Cube,
            "Shaft",
            monument.transform,
            new Vector3(0f, 3.5f, 0f),
            new Vector3(1.2f, 5f, 1.2f),
            stoneMaterial
        );
        CreatePrimitive(
            PrimitiveType.Cube,
            "Tip",
            monument.transform,
            new Vector3(0f, 6.4f, 0f),
            new Vector3(0.9f, 0.9f, 0.9f),
            accentMaterial,
            Quaternion.Euler(45f, 0f, 45f)
        );
    }

    /// <summary>
    /// The lake: a basin sunk below the shore with a sheet of water laid over it, a jetty to
    /// stand on and nothing to fight. Walking in leaves you knee deep, which is as much
    /// swimming as this game needs.
    /// </summary>
    static void BuildLake()
    {
        var lake = new GameObject("Lake");
        lake.transform.position = LakeCentre;

        Material shoreMaterial = CreateMaterial("LakeShore", new Color(0.74f, 0.68f, 0.50f));
        Material waterMaterial = CreateMaterial("LakeWater", new Color(0.20f, 0.45f, 0.70f));
        Material bedMaterial = CreateMaterial("LakeBed", new Color(0.32f, 0.30f, 0.26f));
        Material reedMaterial = CreateMaterial("LakeReed", new Color(0.38f, 0.62f, 0.30f));

        const float half = AreaHalfWidth;
        const float lakeRadius = 17f;
        const float lakeZ = 4f;

        // The field is square; only the hole in it is round. A ring of slabs turned to face
        // the middle makes the round edge, and four slabs fill the square out to the walls.
        const int segments = 32;
        const float ringOuter = 25f;
        for (int i = 0; i < segments; i++)
        {
            float angle = i * 360f / segments;
            float radians = angle * Mathf.Deg2Rad;
            float middle = (lakeRadius + ringOuter) * 0.5f;

            CreatePrimitive(
                PrimitiveType.Cube,
                $"Bank{i}",
                lake.transform,
                new Vector3(
                    Mathf.Cos(radians) * middle,
                    -0.5f,
                    lakeZ + Mathf.Sin(radians) * middle
                ),
                new Vector3(ringOuter - lakeRadius, 1f, 2f * Mathf.PI * ringOuter / segments + 1f),
                shoreMaterial,
                Quaternion.Euler(0f, -angle, 0f)
            );
        }

        // Everything outside a square that the ring already covers to its corners.
        const float inner = ringOuter * 0.70f;
        CreatePrimitive(
            PrimitiveType.Cube,
            "ShoreNorth",
            lake.transform,
            new Vector3(0f, -0.5f, (lakeZ + inner + half) * 0.5f),
            new Vector3(half * 2f, 1f, half - lakeZ - inner),
            shoreMaterial
        );
        CreatePrimitive(
            PrimitiveType.Cube,
            "ShoreSouth",
            lake.transform,
            new Vector3(0f, -0.5f, (lakeZ - inner - half) * 0.5f),
            new Vector3(half * 2f, 1f, half + lakeZ - inner),
            shoreMaterial
        );
        CreatePrimitive(
            PrimitiveType.Cube,
            "ShoreWest",
            lake.transform,
            new Vector3((-inner - half) * 0.5f, -0.5f, lakeZ),
            new Vector3(half - inner, 1f, inner * 2f),
            shoreMaterial
        );
        CreatePrimitive(
            PrimitiveType.Cube,
            "ShoreEast",
            lake.transform,
            new Vector3((inner + half) * 0.5f, -0.5f, lakeZ),
            new Vector3(half - inner, 1f, inner * 2f),
            shoreMaterial
        );
        Material wallMaterial = CreateMaterial("Wall", new Color(0.30f, 0.32f, 0.38f));
        BuildBounds(lake.transform, "", wallMaterial);

        // The water: a disc lying on the ground, and a darker one under it for depth. Neither
        // has a collider, so you wade through rather than walk on top.
        // The bed is square and solid; every corner of it hides under the ring above, and it
        // is what stops you falling through the lake.
        CreatePrimitive(
            PrimitiveType.Cube,
            "Bed",
            lake.transform,
            new Vector3(0f, -1.5f, lakeZ),
            new Vector3(lakeRadius * 2f, 1f, lakeRadius * 2f),
            bedMaterial
        );

        // The surface: a disc with no collider, so you wade in instead of walking over.
        CreateDisc(
            "Water",
            lake.transform,
            new Vector3(0f, FishingSpot.WaterHeight, lakeZ),
            lakeRadius * 2f,
            waterMaterial
        );

        // Reeds along the bank, on the shore side of the waterline.
        for (int i = 0; i < 24; i++)
        {
            float radians = i * 15f * Mathf.Deg2Rad;
            BodyPart(
                PrimitiveType.Cube,
                $"Reed{i}",
                lake.transform,
                new Vector3(
                    Mathf.Cos(radians) * (lakeRadius + 0.7f),
                    0.5f,
                    lakeZ + Mathf.Sin(radians) * (lakeRadius + 0.7f)
                ),
                new Vector3(0.18f, 1f, 0.18f),
                reedMaterial,
                Quaternion.Euler(i % 3 * 6f, i * 24f, i % 5 * 5f)
            );
        }

        var spot = new GameObject("FishingSpot");
        spot.transform.SetParent(lake.transform, false);
        spot.transform.localPosition = new Vector3(0f, 0f, lakeZ);
        spot.AddComponent<FishingSpot>();

        BuildWarpPad(
            lake.transform,
            "WarpPadLakeExit",
            new Vector3(0f, 0f, -25f),
            "마을",
            VillageLakeArrival,
            "PortalVillage",
            new Color(0.30f, 0.80f, 0.85f)
        );
    }

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

        var ground = CreatePrimitive(
            PrimitiveType.Plane,
            "ArenaGround",
            arena.transform,
            Vector3.zero,
            new Vector3(6f, 1f, 6f),
            stoneMaterial
        );
        ground.isStatic = true;

        BuildBounds(arena.transform, "Arena", wallMaterial);

        // The floor of the ring, a low step up so it reads as a stage.
        CreatePrimitive(
            PrimitiveType.Cube,
            "Ring",
            arena.transform,
            new Vector3(0f, 0.17f, 0f),
            new Vector3(ArenaRadius * 2f, 0.35f, ArenaRadius * 2f),
            sandMaterial
        );
        CreateDisc(
            "RingMark",
            arena.transform,
            new Vector3(0f, 0.36f, 0f),
            ArenaRadius * 1.9f,
            sandMaterial
        );
        CreateDisc("Centre", arena.transform, new Vector3(0f, 0.37f, 0f), 4f, trimMaterial);

        // Low wall around the ring: twelve segments turned to follow the circle.
        for (int i = 0; i < 12; i++)
        {
            float angle = i * Mathf.PI * 2f / 12f;
            CreatePrimitive(
                PrimitiveType.Cube,
                $"RingWall{i}",
                arena.transform,
                new Vector3(Mathf.Cos(angle) * ArenaRadius, 0.85f, Mathf.Sin(angle) * ArenaRadius),
                new Vector3(ArenaRadius * 0.58f, 1f, 0.6f),
                trimMaterial,
                Quaternion.Euler(0f, -angle * Mathf.Rad2Deg + 90f, 0f)
            );
        }

        // Corner marks the duel starts on.
        CreateDisc(
            "CornerA",
            arena.transform,
            CornerA - ArenaCentre + new Vector3(0f, -0.62f, 0f),
            4f,
            redMaterial
        );
        CreateDisc(
            "CornerB",
            arena.transform,
            CornerB - ArenaCentre + new Vector3(0f, -0.62f, 0f),
            4f,
            blueMaterial
        );

        // Seating on the east and west sides, three tiers each.
        foreach (float side in new[] { -1f, 1f })
        {
            for (int tier = 0; tier < 3; tier++)
            {
                CreatePrimitive(
                    PrimitiveType.Cube,
                    side < 0f ? $"StandWest{tier}" : $"StandEast{tier}",
                    arena.transform,
                    new Vector3(side * (ArenaRadius + 2.5f + tier * 2f), 0.4f + tier * 0.8f, 0f),
                    new Vector3(2f, 0.8f + tier * 1.6f, ArenaRadius * 1.6f),
                    stoneMaterial
                );
            }
        }

        // Torch posts at the four compass points.
        for (int i = 0; i < 4; i++)
        {
            float angle = i * Mathf.PI * 0.5f + Mathf.PI * 0.25f;
            var post = new GameObject($"Torch{i}");
            post.transform.SetParent(arena.transform, false);
            post.transform.localPosition = new Vector3(
                Mathf.Cos(angle) * (ArenaRadius + 1.2f),
                0f,
                Mathf.Sin(angle) * (ArenaRadius + 1.2f)
            );
            CreatePrimitive(
                PrimitiveType.Cube,
                "Post",
                post.transform,
                new Vector3(0f, 1.6f, 0f),
                new Vector3(0.35f, 3.2f, 0.35f),
                trimMaterial
            );
            BodyPart(
                PrimitiveType.Cube,
                "Flame",
                post.transform,
                new Vector3(0f, 3.5f, 0f),
                new Vector3(0.5f, 0.6f, 0.5f),
                torchMaterial
            );
        }

        BuildWarpPad(
            arena.transform,
            "WarpPadArenaExit",
            ArenaPad - ArenaCentre,
            "마을",
            VillageArenaArrival,
            "PortalVillage",
            new Color(0.30f, 0.80f, 0.85f)
        );

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
    static void BuildChest(
        Transform parent,
        string name,
        Vector3 localPosition,
        int gold,
        int exp,
        int ore,
        int piece
    )
    {
        Material woodMaterial = CreateMaterial("ChestWood", new Color(0.42f, 0.28f, 0.16f));
        Material bandMaterial = CreateMaterial("ChestBand", new Color(0.80f, 0.66f, 0.28f));

        var chest = new GameObject(name);
        chest.transform.SetParent(parent, false);
        chest.transform.localPosition = localPosition;

        CreatePrimitive(
            PrimitiveType.Cube,
            "Body",
            chest.transform,
            new Vector3(0f, 0.3f, 0f),
            new Vector3(1.4f, 0.6f, 0.9f),
            woodMaterial
        );
        BodyPart(
            PrimitiveType.Cube,
            "BandLeft",
            chest.transform,
            new Vector3(-0.45f, 0.3f, 0f),
            new Vector3(0.12f, 0.64f, 0.94f),
            bandMaterial
        );
        BodyPart(
            PrimitiveType.Cube,
            "BandRight",
            chest.transform,
            new Vector3(0.45f, 0.3f, 0f),
            new Vector3(0.12f, 0.64f, 0.94f),
            bandMaterial
        );
        BodyPart(
            PrimitiveType.Cube,
            "Lock",
            chest.transform,
            new Vector3(0f, 0.42f, -0.47f),
            new Vector3(0.22f, 0.22f, 0.08f),
            bandMaterial
        );

        var lid = new GameObject("Lid");
        lid.transform.SetParent(chest.transform, false);
        lid.transform.localPosition = new Vector3(0f, 0.6f, 0.45f);
        BodyPart(
            PrimitiveType.Cube,
            "LidBoard",
            lid.transform,
            new Vector3(0f, 0.1f, -0.45f),
            new Vector3(1.4f, 0.2f, 0.9f),
            woodMaterial
        );
        BodyPart(
            PrimitiveType.Cube,
            "LidBand",
            lid.transform,
            new Vector3(0f, 0.21f, -0.45f),
            new Vector3(0.2f, 0.06f, 0.94f),
            bandMaterial
        );

        chest.AddComponent<NetworkObject>();
        var treasure = chest.AddComponent<TreasureChest>();
        treasure.Lid = lid.transform;
        treasure.Gold = gold;
        treasure.Exp = exp;
        treasure.Ore = ore;
        treasure.Piece = piece;
    }

    /// <summary>Anvil, campfire and quest board, all within sight of the plaza.</summary>
    static void BuildStations(Transform parent)
    {
        Material woodMaterial = CreateMaterial("Wood", new Color(0.45f, 0.32f, 0.20f));
        Material emberMaterial = CreateMaterial("Ember", new Color(0.95f, 0.55f, 0.20f));

        BuildAnvil(parent, new Vector3(-5f, 0f, 10f), woodMaterial);
        BuildDresser(parent, new Vector3(13f, 0f, -4f));
        BuildCampfire(parent, new Vector3(6f, 0f, 9f), woodMaterial, emberMaterial);

        var board = new GameObject("QuestBoard");
        board.transform.SetParent(parent, false);
        board.transform.localPosition = new Vector3(0f, 0f, 12f);
        CreatePrimitive(
            PrimitiveType.Cube,
            "PostLeft",
            board.transform,
            new Vector3(-0.9f, 0.7f, 0f),
            new Vector3(0.15f, 1.4f, 0.15f),
            woodMaterial
        );
        CreatePrimitive(
            PrimitiveType.Cube,
            "PostRight",
            board.transform,
            new Vector3(0.9f, 0.7f, 0f),
            new Vector3(0.15f, 1.4f, 0.15f),
            woodMaterial
        );
        CreatePrimitive(
            PrimitiveType.Cube,
            "Board",
            board.transform,
            new Vector3(0f, 1.5f, 0f),
            new Vector3(2.2f, 1.2f, 0.12f),
            woodMaterial
        );
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
        CreatePrimitive(
            PrimitiveType.Cube,
            "Stump",
            anvil.transform,
            new Vector3(0f, 0.35f, 0f),
            new Vector3(1f, 0.7f, 1f),
            woodMaterial
        );
        CreatePrimitive(
            PrimitiveType.Cube,
            "Foot",
            anvil.transform,
            new Vector3(0f, 0.79f, 0f),
            new Vector3(0.9f, 0.18f, 0.5f),
            ironMaterial
        );
        CreatePrimitive(
            PrimitiveType.Cube,
            "Waist",
            anvil.transform,
            new Vector3(0f, 0.99f, 0f),
            new Vector3(0.45f, 0.24f, 0.36f),
            ironMaterial
        );
        CreatePrimitive(
            PrimitiveType.Cube,
            "Plate",
            anvil.transform,
            new Vector3(0f, 1.19f, 0f),
            new Vector3(1.15f, 0.17f, 0.55f),
            ironMaterial
        );
        BodyPart(
            PrimitiveType.Cube,
            "Horn",
            anvil.transform,
            new Vector3(0.78f, 1.19f, 0f),
            new Vector3(0.55f, 0.13f, 0.24f),
            ironMaterial,
            Quaternion.Euler(0f, 0f, -6f)
        );

        // Hammer resting across the plate.
        BodyPart(
            PrimitiveType.Cube,
            "HammerHandle",
            anvil.transform,
            new Vector3(-0.28f, 1.32f, 0.05f),
            new Vector3(0.07f, 0.5f, 0.07f),
            handleMaterial,
            Quaternion.Euler(0f, 20f, 78f)
        );
        BodyPart(
            PrimitiveType.Cube,
            "HammerHead",
            anvil.transform,
            new Vector3(-0.02f, 1.34f, 0.02f),
            new Vector3(0.2f, 0.17f, 0.17f),
            ironMaterial
        );

        // Tool rack behind, with three blanks hanging from the crossbar.
        CreatePrimitive(
            PrimitiveType.Cube,
            "RackPostLeft",
            anvil.transform,
            new Vector3(-1f, 0.9f, -0.9f),
            new Vector3(0.12f, 1.8f, 0.12f),
            woodMaterial
        );
        CreatePrimitive(
            PrimitiveType.Cube,
            "RackPostRight",
            anvil.transform,
            new Vector3(1f, 0.9f, -0.9f),
            new Vector3(0.12f, 1.8f, 0.12f),
            woodMaterial
        );
        BodyPart(
            PrimitiveType.Cube,
            "RackBar",
            anvil.transform,
            new Vector3(0f, 1.7f, -0.9f),
            new Vector3(2.1f, 0.12f, 0.12f),
            woodMaterial
        );
        for (int i = 0; i < 3; i++)
        {
            BodyPart(
                PrimitiveType.Cube,
                $"Tool{i}",
                anvil.transform,
                new Vector3(-0.6f + i * 0.6f, 1.3f, -0.9f),
                new Vector3(0.1f, 0.7f, 0.06f),
                ironMaterial
            );
        }

        // Ingots stacked next to the stump.
        for (int i = 0; i < 3; i++)
        {
            BodyPart(
                PrimitiveType.Cube,
                $"Ingot{i}",
                anvil.transform,
                new Vector3(-1.3f, 0.09f + i * 0.16f, 0.35f - i * 0.05f),
                new Vector3(0.5f, 0.15f, 0.28f),
                ingotMaterial
            );
        }

        var station = anvil.AddComponent<CraftStation>();
        station.Title = "Anvil";
        station.PromptHeight = 2.1f;
    }

    /// <summary>
    /// A cooking fire: ring of stones, crossed logs, layered flames, a pot on a tripod and
    /// two logs to sit on. Reads as a place to cook rather than a lit box.
    /// </summary>
    static void BuildCampfire(
        Transform parent,
        Vector3 localPosition,
        Material woodMaterial,
        Material emberMaterial
    )
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
            CreatePrimitive(
                PrimitiveType.Sphere,
                $"Stone{i}",
                fire.transform,
                new Vector3(Mathf.Cos(angle) * 1.05f, 0.16f, Mathf.Sin(angle) * 1.05f),
                new Vector3(0.42f, 0.32f, 0.42f),
                stoneMaterial,
                Quaternion.Euler(0f, i * 31f, 0f)
            );
        }

        // Crossed logs in the middle.
        for (int i = 0; i < 3; i++)
        {
            BodyPart(
                PrimitiveType.Cylinder,
                $"Log{i}",
                fire.transform,
                new Vector3(0f, 0.3f, 0f),
                new Vector3(0.16f, 0.6f, 0.16f),
                woodMaterial,
                Quaternion.Euler(62f, i * 60f, 0f)
            );
        }

        // Flames: three shrinking blocks, turned so they never look like a stacked box.
        BodyPart(
            PrimitiveType.Cube,
            "FlameLow",
            fire.transform,
            new Vector3(0f, 0.55f, 0f),
            new Vector3(0.7f, 0.7f, 0.7f),
            emberMaterial,
            Quaternion.Euler(0f, 25f, 0f)
        );
        BodyPart(
            PrimitiveType.Cube,
            "FlameMid",
            fire.transform,
            new Vector3(0.06f, 0.95f, -0.04f),
            new Vector3(0.48f, 0.55f, 0.48f),
            emberMaterial,
            Quaternion.Euler(0f, -15f, 8f)
        );
        BodyPart(
            PrimitiveType.Cube,
            "FlameTip",
            fire.transform,
            new Vector3(-0.05f, 1.28f, 0.05f),
            new Vector3(0.28f, 0.38f, 0.28f),
            flameMaterial,
            Quaternion.Euler(0f, 40f, -10f)
        );

        // Tripod with a pot hanging over the fire.
        for (int i = 0; i < 3; i++)
        {
            float angle = i * Mathf.PI * 2f / 3f;
            BodyPart(
                PrimitiveType.Cube,
                $"TripodLeg{i}",
                fire.transform,
                new Vector3(Mathf.Cos(angle) * 0.55f, 0.9f, Mathf.Sin(angle) * 0.55f),
                new Vector3(0.09f, 1.9f, 0.09f),
                woodMaterial,
                Quaternion.Euler(Mathf.Sin(angle) * 18f, 0f, -Mathf.Cos(angle) * 18f)
            );
        }

        BodyPart(
            PrimitiveType.Sphere,
            "Pot",
            fire.transform,
            new Vector3(0f, 1.05f, 0f),
            new Vector3(0.62f, 0.5f, 0.62f),
            potMaterial
        );
        BodyPart(
            PrimitiveType.Cube,
            "PotRim",
            fire.transform,
            new Vector3(0f, 1.28f, 0f),
            new Vector3(0.66f, 0.08f, 0.66f),
            potMaterial
        );

        // Two logs to sit on.
        foreach (float side in new[] { -1f, 1f })
        {
            CreatePrimitive(
                PrimitiveType.Cylinder,
                side < 0f ? "SeatLeft" : "SeatRight",
                fire.transform,
                new Vector3(side * 2f, 0.25f, side * 0.4f),
                new Vector3(0.5f, 1.1f, 0.5f),
                woodMaterial,
                Quaternion.Euler(90f, side * 20f, 0f)
            );
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
            new Vector3(-12f, 0f, 2f),
            new Vector3(10f, 0f, 4f),
            new Vector3(-4f, 0f, -10f),
            new Vector3(6f, 0f, -14f),
            new Vector3(-16f, 0f, 8f),
        };
        for (int i = 0; i < herbs.Length; i++)
        {
            BuildGatherNode(parent, "Herb" + i, herbs[i], GatherKind.Herb);
        }

        var trees = new[]
        {
            new Vector3(18f, 0f, 4f),
            new Vector3(-16f, 0f, -2f),
            new Vector3(4f, 0f, 17f),
            new Vector3(-11f, 0f, 13f),
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
    static void BuildGatherNode(
        Transform parent,
        string name,
        Vector3 localPosition,
        GatherKind kind
    )
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
                CreatePrimitive(
                    PrimitiveType.Sphere,
                    "Stone",
                    node.transform,
                    new Vector3(0f, 0.35f, 0f),
                    new Vector3(1.9f, 0.9f, 1.7f),
                    oreStoneMaterial
                );
                BodyPart(
                    PrimitiveType.Cube,
                    "Crystal",
                    node.transform,
                    new Vector3(-0.35f, 0.85f, 0.1f),
                    new Vector3(0.28f, 1f, 0.28f),
                    oreCrystalMaterial,
                    Quaternion.Euler(0f, 20f, -18f)
                );
                BodyPart(
                    PrimitiveType.Cube,
                    "Crystal",
                    node.transform,
                    new Vector3(0.3f, 1f, -0.15f),
                    new Vector3(0.24f, 1.3f, 0.24f),
                    oreCrystalMaterial,
                    Quaternion.Euler(12f, -30f, 14f)
                );
                BodyPart(
                    PrimitiveType.Cube,
                    "Crystal",
                    node.transform,
                    new Vector3(0.05f, 0.75f, 0.45f),
                    new Vector3(0.2f, 0.8f, 0.2f),
                    oreCrystalMaterial,
                    Quaternion.Euler(-16f, 5f, 22f)
                );
                break;

            case GatherKind.Herb:
                CreatePrimitive(
                    PrimitiveType.Sphere,
                    "Bush",
                    node.transform,
                    new Vector3(0f, 0.35f, 0f),
                    new Vector3(1.1f, 0.7f, 1.1f),
                    herbMaterial
                );
                BodyPart(
                    PrimitiveType.Sphere,
                    "Flower",
                    node.transform,
                    new Vector3(-0.3f, 0.68f, 0.15f),
                    new Vector3(0.22f, 0.22f, 0.22f),
                    flowerMaterial
                );
                BodyPart(
                    PrimitiveType.Sphere,
                    "Flower",
                    node.transform,
                    new Vector3(0.25f, 0.72f, -0.2f),
                    new Vector3(0.2f, 0.2f, 0.2f),
                    flowerMaterial
                );
                BodyPart(
                    PrimitiveType.Sphere,
                    "Flower",
                    node.transform,
                    new Vector3(0.1f, 0.62f, 0.4f),
                    new Vector3(0.18f, 0.18f, 0.18f),
                    flowerMaterial
                );
                break;

            default:
                CreatePrimitive(
                    PrimitiveType.Cylinder,
                    "Trunk",
                    node.transform,
                    new Vector3(0f, 1.2f, 0f),
                    new Vector3(0.5f, 1.2f, 0.5f),
                    trunkMaterial
                );
                CreatePrimitive(
                    PrimitiveType.Sphere,
                    "Leaves",
                    node.transform,
                    new Vector3(0f, 2.9f, 0f),
                    new Vector3(2.6f, 2.2f, 2.6f),
                    leafMaterial
                );
                BodyPart(
                    PrimitiveType.Sphere,
                    "LeavesSide",
                    node.transform,
                    new Vector3(-0.9f, 2.4f, 0.3f),
                    new Vector3(1.5f, 1.3f, 1.5f),
                    leafMaterial
                );
                BodyPart(
                    PrimitiveType.Sphere,
                    "LeavesTop",
                    node.transform,
                    new Vector3(0.4f, 3.6f, -0.2f),
                    new Vector3(1.4f, 1.2f, 1.4f),
                    leafMaterial
                );
                break;
        }

        node.AddComponent<NetworkObject>();
        var gather = node.AddComponent<GatherNode>();
        gather.Kind = kind;
        gather.Yield = kind == GatherKind.Ore ? 1 : 2;
        gather.Exp = kind == GatherKind.Ore ? 5 : 3;
        gather.PromptHeight = kind == GatherKind.Wood ? 3.4f : 1.6f;
    }

    /// <summary>
    /// One head of hair. Style zero has nothing in it, which is what being bald is; the rest
    /// are a cap on the crown and whatever hangs off it, except the mohawk, which is the one
    /// cut that has to leave the sides of the head bare. They sit under the rig rather than the
    /// head because the head is a part, not a pivot, and nothing here has to swing.
    ///
    /// The skull runs from 1.51 to 1.95 and is 0.44 across, which is what every number below
    /// is measured against.
    /// </summary>
    static GameObject BuildHairStyle(Transform rig, int style, Material material)
    {
        var hair = new GameObject("Hair" + style);
        hair.transform.SetParent(rig, false);

        if (style == 0)
        {
            return hair;
        }

        if (style == 6)
        {
            // Shaved at the sides, so no cap: a ridge front to back, tallest in the middle.
            float[] heights = { 0.24f, 0.34f, 0.30f, 0.20f };
            for (int i = 0; i < heights.Length; i++)
            {
                BodyPart(
                    PrimitiveType.Cube,
                    $"Ridge{i}",
                    hair.transform,
                    new Vector3(0f, 1.94f + heights[i] * 0.5f, 0.16f - i * 0.12f),
                    new Vector3(0.11f, heights[i], 0.13f),
                    material
                );
            }

            return hair;
        }

        BodyPart(
            PrimitiveType.Cube,
            "Cap",
            hair.transform,
            new Vector3(0f, 1.94f, -0.01f),
            new Vector3(0.47f, 0.13f, 0.45f),
            material
        );
        BodyPart(
            PrimitiveType.Cube,
            "Fringe",
            hair.transform,
            new Vector3(0f, 1.85f, 0.2f),
            new Vector3(0.46f, 0.12f, 0.06f),
            material
        );

        switch (style)
        {
            case 2:
                // Long: down the back and past the ears.
                BodyPart(
                    PrimitiveType.Cube,
                    "Back",
                    hair.transform,
                    new Vector3(0f, 1.76f, -0.21f),
                    new Vector3(0.44f, 0.3f, 0.07f),
                    material
                );
                foreach (float side in new[] { -1f, 1f })
                {
                    BodyPart(
                        PrimitiveType.Cube,
                        side < 0f ? "SideLeft" : "SideRight",
                        hair.transform,
                        new Vector3(side * 0.215f, 1.76f, -0.02f),
                        new Vector3(0.06f, 0.3f, 0.4f),
                        material
                    );
                }

                break;

            case 3:
                // Spiked: three points, leaning further out the further from the middle.
                for (int i = 0; i < 3; i++)
                {
                    float across = (i - 1) * 0.15f;
                    BodyPart(
                        PrimitiveType.Cube,
                        $"Spike{i}",
                        hair.transform,
                        new Vector3(across, 2.06f, -0.02f),
                        new Vector3(0.11f, 0.26f, 0.11f),
                        material,
                        Quaternion.Euler(-18f, 0f, across * 90f)
                    );
                }

                break;

            case 4:
                // Tied at the back, hanging down and away.
                BodyPart(
                    PrimitiveType.Cube,
                    "Tie",
                    hair.transform,
                    new Vector3(0f, 1.87f, -0.24f),
                    new Vector3(0.13f, 0.12f, 0.1f),
                    material
                );
                BodyPart(
                    PrimitiveType.Cube,
                    "Tail",
                    hair.transform,
                    new Vector3(0f, 1.68f, -0.31f),
                    new Vector3(0.14f, 0.42f, 0.14f),
                    material,
                    Quaternion.Euler(18f, 0f, 0f)
                );
                break;

            case 5:
                // Gathered into a knot on top.
                BodyPart(
                    PrimitiveType.Cube,
                    "Band",
                    hair.transform,
                    new Vector3(0f, 2.0f, -0.02f),
                    new Vector3(0.17f, 0.06f, 0.17f),
                    material
                );
                BodyPart(
                    PrimitiveType.Sphere,
                    "Bun",
                    hair.transform,
                    new Vector3(0f, 2.1f, -0.02f),
                    new Vector3(0.23f, 0.21f, 0.23f),
                    material
                );
                break;

            case 7:
                // A poofy, boxy bob: side panels angled outward toward the bottom instead of a
                // sleek taper, so it flares rather than pressing flat against the head.
                foreach (float side in new[] { -1f, 1f })
                {
                    BodyPart(
                        PrimitiveType.Cube,
                        side < 0f ? "FlareLeft" : "FlareRight",
                        hair.transform,
                        new Vector3(side * 0.3f, 1.72f, -0.02f),
                        new Vector3(0.14f, 0.34f, 0.4f),
                        material
                    );
                }
                BodyPart(
                    PrimitiveType.Cube,
                    "Back",
                    hair.transform,
                    new Vector3(0f, 1.72f, -0.24f),
                    new Vector3(0.5f, 0.34f, 0.16f),
                    material
                );
                break;
        }

        return hair;
    }

    /// <summary>A standing mirror: a frame with a pale sheet in it, and somewhere to change.</summary>
    static void BuildDresser(Transform parent, Vector3 localPosition)
    {
        Material frameMaterial = CreateMaterial("MirrorFrame", new Color(0.44f, 0.30f, 0.20f));
        Material glassMaterial = CreateMaterial("MirrorGlass", new Color(0.78f, 0.88f, 0.94f));

        var mirror = new GameObject("Mirror");
        mirror.transform.SetParent(parent, false);
        mirror.transform.localPosition = localPosition;
        // Glass towards the middle of the plaza, which is the side anyone walks up from.
        mirror.transform.localRotation = Quaternion.Euler(0f, -73f, 0f);

        CreatePrimitive(
            PrimitiveType.Cube,
            "Foot",
            mirror.transform,
            new Vector3(0f, 0.1f, 0f),
            new Vector3(1.3f, 0.2f, 0.5f),
            frameMaterial
        );
        BodyPart(
            PrimitiveType.Cube,
            "Glass",
            mirror.transform,
            new Vector3(0f, 1.4f, 0.03f),
            new Vector3(0.95f, 2f, 0.08f),
            glassMaterial
        );

        foreach (float side in new[] { -1f, 1f })
        {
            CreatePrimitive(
                PrimitiveType.Cube,
                side < 0f ? "PostLeft" : "PostRight",
                mirror.transform,
                new Vector3(side * 0.58f, 1.4f, 0f),
                new Vector3(0.16f, 2.3f, 0.2f),
                frameMaterial
            );
        }

        BodyPart(
            PrimitiveType.Cube,
            "Head",
            mirror.transform,
            new Vector3(0f, 2.58f, 0f),
            new Vector3(1.32f, 0.18f, 0.2f),
            frameMaterial
        );
        BodyPart(
            PrimitiveType.Cube,
            "Sill",
            mirror.transform,
            new Vector3(0f, 0.28f, 0f),
            new Vector3(1.32f, 0.16f, 0.24f),
            frameMaterial
        );

        var dresser = mirror.AddComponent<Dresser>();
        dresser.PromptHeight = 2.9f;
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

        var shopkeeper = BuildHumanoid(
            npc.transform,
            coatMaterial,
            skinMaterial,
            pantsMaterial,
            faceMaterial
        );
        BuildHairStyle(
            shopkeeper.Rig,
            1,
            CreateMaterial("NpcHair", new Color(0.30f, 0.20f, 0.14f))
        );

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
    static WarpPad BuildWarpPad(
        Transform parent,
        string name,
        Vector3 position,
        string label,
        Vector3 destination,
        string portalName,
        Color portalColor
    )
    {
        Material stoneMaterial = CreateMaterial("GateStone", new Color(0.55f, 0.55f, 0.60f));
        Material trimMaterial = CreateMaterial("GateTrim", new Color(0.36f, 0.36f, 0.42f));
        Material portalMaterial = CreateMaterial(portalName, portalColor);

        var pad = new GameObject(name);
        pad.transform.SetParent(parent, false);
        pad.transform.localPosition = position;

        // Just over the plaza, which is itself a disc two centimetres up: enough not to
        // fight it for the same pixels, not enough to look propped up.
        CreateDisc("Base", pad.transform, new Vector3(0f, 0.03f, 0f), 5f, trimMaterial);
        CreateDisc("Inlay", pad.transform, new Vector3(0f, 0.05f, 0f), 3.4f, portalMaterial);

        // Two stepped pillars and a lintel across the top.
        foreach (float side in new[] { -1f, 1f })
        {
            CreatePrimitive(
                PrimitiveType.Cube,
                side < 0f ? "PillarLeft" : "PillarRight",
                pad.transform,
                new Vector3(side * 2.3f, 2f, 0f),
                new Vector3(0.7f, 4f, 0.7f),
                stoneMaterial
            );
            CreatePrimitive(
                PrimitiveType.Cube,
                side < 0f ? "FootLeft" : "FootRight",
                pad.transform,
                new Vector3(side * 2.3f, 0.25f, 0f),
                new Vector3(1.1f, 0.5f, 1.1f),
                trimMaterial
            );
            CreatePrimitive(
                PrimitiveType.Cube,
                side < 0f ? "CapLeft" : "CapRight",
                pad.transform,
                new Vector3(side * 2.3f, 4.1f, 0f),
                new Vector3(1f, 0.35f, 1f),
                trimMaterial
            );
        }

        CreatePrimitive(
            PrimitiveType.Cube,
            "Lintel",
            pad.transform,
            new Vector3(0f, 4.5f, 0f),
            new Vector3(5.6f, 0.6f, 0.9f),
            stoneMaterial
        );

        // The glowing sheet, and a rune floating above the arch. Neither blocks the way.
        BodyPart(
            PrimitiveType.Cube,
            "Portal",
            pad.transform,
            // Down onto the plate: the sheet used to start a hand above the ground, which
            // read as the whole gate hovering.
            new Vector3(0f, 2.03f, 0f),
            new Vector3(3.6f, 4f, 0.12f),
            portalMaterial
        );
        BodyPart(
            PrimitiveType.Cube,
            "Rune",
            pad.transform,
            new Vector3(0f, 5.2f, 0f),
            new Vector3(0.55f, 0.55f, 0.55f),
            portalMaterial,
            Quaternion.Euler(45f, 45f, 0f)
        );

        var warp = pad.AddComponent<WarpPad>();
        warp.Destination = destination;
        warp.Label = label;
        return warp;
    }

    /// <summary>
    /// A walled dungeon: a guarded corridor and the boss chamber at the end. The only way
    /// out is the gate home - dungeons never connect to each other, so every run starts
    /// from the village.
    /// Shared, not instanced - the scene is single, so everyone meets the same boss.
    /// There is no ceiling on purpose: the camera does not handle being enclosed.
    /// </summary>
    static void BuildDungeon(GameObject monsterPrefab, DungeonArea area)
    {
        var dungeon = new GameObject("Dungeon" + area.Key);
        dungeon.transform.position = area.Center;

        Material floorMaterial = CreateMaterial("DungeonFloor" + area.Key, area.Floor);
        Material stoneMaterial = CreateMaterial("DungeonStone" + area.Key, area.Stone);
        Material torchMaterial = CreateMaterial("Torch" + area.Key, area.Torch);
        var floor = CreatePrimitive(
            PrimitiveType.Plane,
            "DungeonFloor",
            dungeon.transform,
            Vector3.zero,
            new Vector3(6f, 1f, 6f),
            floorMaterial
        );
        floor.isStatic = true;

        // Outer walls, taller than the village so it reads as indoors.
        const float half = AreaHalfWidth;
        const float wallHeight = 8f;
        CreatePrimitive(
            PrimitiveType.Cube,
            "OuterNorth",
            dungeon.transform,
            new Vector3(0f, wallHeight * 0.5f, half),
            new Vector3(60f, wallHeight, 1f),
            stoneMaterial
        );
        CreatePrimitive(
            PrimitiveType.Cube,
            "OuterSouth",
            dungeon.transform,
            new Vector3(0f, wallHeight * 0.5f, -half),
            new Vector3(60f, wallHeight, 1f),
            stoneMaterial
        );
        CreatePrimitive(
            PrimitiveType.Cube,
            "OuterEast",
            dungeon.transform,
            new Vector3(half, wallHeight * 0.5f, 0f),
            new Vector3(1f, wallHeight, 60f),
            stoneMaterial
        );
        CreatePrimitive(
            PrimitiveType.Cube,
            "OuterWest",
            dungeon.transform,
            new Vector3(-half, wallHeight * 0.5f, 0f),
            new Vector3(1f, wallHeight, 60f),
            stoneMaterial
        );

        // The corridor: two long walls that funnel everyone past the guards into the chamber.
        CreatePrimitive(
            PrimitiveType.Cube,
            "CorridorWest",
            dungeon.transform,
            new Vector3(-7f, wallHeight * 0.5f, -15f),
            new Vector3(1f, wallHeight, 30f),
            stoneMaterial
        );
        CreatePrimitive(
            PrimitiveType.Cube,
            "CorridorEast",
            dungeon.transform,
            new Vector3(7f, wallHeight * 0.5f, -15f),
            new Vector3(1f, wallHeight, 30f),
            stoneMaterial
        );

        // Chamber walls, open towards the corridor mouth.
        CreatePrimitive(
            PrimitiveType.Cube,
            "ChamberWest",
            dungeon.transform,
            new Vector3(-18f, wallHeight * 0.5f, 8f),
            new Vector3(1f, wallHeight, 34f),
            stoneMaterial
        );
        CreatePrimitive(
            PrimitiveType.Cube,
            "ChamberEast",
            dungeon.transform,
            new Vector3(18f, wallHeight * 0.5f, 8f),
            new Vector3(1f, wallHeight, 34f),
            stoneMaterial
        );

        for (int i = 0; i < 6; i++)
        {
            float z = -26f + i * 5f;
            CreatePrimitive(
                PrimitiveType.Cube,
                $"TorchWest{i}",
                dungeon.transform,
                new Vector3(-6.2f, 3.2f, z),
                new Vector3(0.3f, 0.6f, 0.3f),
                torchMaterial
            );
            CreatePrimitive(
                PrimitiveType.Cube,
                $"TorchEast{i}",
                dungeon.transform,
                new Vector3(6.2f, 3.2f, z),
                new Vector3(0.3f, 0.6f, 0.3f),
                torchMaterial
            );
        }

        // Pedestal the boss stands on, so the chamber has an obvious centre.
        CreatePrimitive(
            PrimitiveType.Cube,
            "Pedestal",
            dungeon.transform,
            new Vector3(0f, 0.15f, 14f),
            new Vector3(12f, 0.3f, 12f),
            stoneMaterial
        );

        var guards = new GameObject("DungeonGuards");
        guards.transform.SetParent(dungeon.transform, false);
        guards.transform.localPosition = new Vector3(0f, 0f, -14f);
        var guardSpawner = guards.AddComponent<MonsterSpawner>();
        guardSpawner.MonsterPrefab = monsterPrefab;
        guardSpawner.Count = 6;
        guardSpawner.Radius = 5f;
        guardSpawner.LevelBonus = area.GuardBonus;
        guardSpawner.Theme = area.Theme;

        // Two chests flanking the pedestal, shut until the boss goes down: the weapon of this
        // ground on the left, its armour on the right.
        BuildChest(
            dungeon.transform,
            "ChestLeft",
            new Vector3(-4.5f, 0.3f, 14f),
            area.ChestGold,
            area.ChestExp,
            area.ChestOre,
            PlayerGear.PieceFor(area.Theme, true)
        );
        BuildChest(
            dungeon.transform,
            "ChestRight",
            new Vector3(4.5f, 0.3f, 14f),
            area.ChestGold,
            area.ChestExp,
            area.ChestOre,
            PlayerGear.PieceFor(area.Theme, false)
        );

        var bossPoint = new GameObject("BossSpawner");
        bossPoint.transform.SetParent(dungeon.transform, false);
        bossPoint.transform.localPosition = new Vector3(0f, 0f, 14f);
        var bossSpawner = bossPoint.AddComponent<MonsterSpawner>();
        bossSpawner.MonsterPrefab = monsterPrefab;
        bossSpawner.LevelBonus = area.BossBonus;
        bossSpawner.Theme = area.Theme;
        bossSpawner.Boss = true;

        BuildWarpPad(
            dungeon.transform,
            "WarpPadDungeonExit" + area.Key,
            new Vector3(0f, 0f, -26f),
            "마을",
            VillageDungeonArrival,
            "PortalVillage",
            new Color(0.30f, 0.80f, 0.85f)
        );
    }

    /// <summary>
    /// A hunting ground: open floor, walls, scenery in the area's own palette, monsters and
    /// the one gate home. Grounds never connect to each other; the village gate is the only
    /// way in.
    /// </summary>
    static void BuildHuntingField(GameObject monsterPrefab, FieldArea area)
    {
        var field = new GameObject("HuntingField" + area.Key);
        field.transform.position = area.Center;

        Material fieldMaterial = CreateMaterial("FieldGround" + area.Key, area.Ground);
        Material wallMaterial = CreateMaterial("Wall" + area.Key, area.Wall);

        var ground = CreatePrimitive(
            PrimitiveType.Plane,
            "FieldGround",
            field.transform,
            Vector3.zero,
            new Vector3(6f, 1f, 6f),
            fieldMaterial
        );
        ground.isStatic = true;

        BuildBounds(field.transform, "Field", wallMaterial);

        // Field scenery is all natural shapes: round boulders, bare dead trees and grass
        // tufts. Nothing here is a box, so it never gets confused with village masonry.
        Material bouldMaterial = CreateMaterial("Boulder" + area.Key, area.Rock);
        Material deadWoodMaterial = CreateMaterial("DeadWood" + area.Key, area.Wood);
        Material tuftMaterial = CreateMaterial("GrassTuft" + area.Key, area.Tuft);
        Material propMaterial = CreateMaterial("FieldProp" + area.Key, area.Prop);

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
            CreatePrimitive(
                PrimitiveType.Sphere,
                $"Boulder{i}",
                field.transform,
                new Vector3(boulders[i].position.x, size * 0.3f, boulders[i].position.z),
                new Vector3(size, size * 0.75f, size * 0.9f),
                bouldMaterial,
                Quaternion.Euler(0f, i * 37f, 8f)
            );
            BodyPart(
                PrimitiveType.Sphere,
                $"BoulderChip{i}",
                field.transform,
                new Vector3(
                    boulders[i].position.x + size * 0.55f,
                    size * 0.18f,
                    boulders[i].position.z - size * 0.4f
                ),
                new Vector3(size * 0.45f, size * 0.35f, size * 0.4f),
                bouldMaterial
            );
        }

        var deadTrees = new[]
        {
            new Vector3(-20f, 0f, 2f),
            new Vector3(7f, 0f, -22f),
            new Vector3(24f, 0f, 6f),
        };
        for (int i = 0; i < deadTrees.Length; i++)
        {
            var tree = new GameObject($"DeadTree{i}");
            tree.transform.SetParent(field.transform, false);
            tree.transform.localPosition = deadTrees[i];
            CreatePrimitive(
                PrimitiveType.Cylinder,
                "Trunk",
                tree.transform,
                new Vector3(0f, 2f, 0f),
                new Vector3(0.45f, 2f, 0.45f),
                deadWoodMaterial
            );
            BodyPart(
                PrimitiveType.Cylinder,
                "BranchLeft",
                tree.transform,
                new Vector3(-0.7f, 3f, 0f),
                new Vector3(0.2f, 0.9f, 0.2f),
                deadWoodMaterial,
                Quaternion.Euler(0f, 0f, 55f)
            );
            BodyPart(
                PrimitiveType.Cylinder,
                "BranchRight",
                tree.transform,
                new Vector3(0.7f, 3.4f, 0.1f),
                new Vector3(0.18f, 0.8f, 0.18f),
                deadWoodMaterial,
                Quaternion.Euler(10f, 0f, -50f)
            );
        }

        for (int i = 0; i < 14; i++)
        {
            float angle = i * 137f * Mathf.Deg2Rad;
            float radius = 6f + (i % 5) * 4f;
            BodyPart(
                PrimitiveType.Cube,
                $"Grass{i}",
                field.transform,
                new Vector3(Mathf.Cos(angle) * radius, 0.25f, Mathf.Sin(angle) * radius),
                new Vector3(0.5f, 0.5f, 0.5f),
                tuftMaterial,
                Quaternion.Euler(0f, i * 23f, 12f)
            );
        }

        // One prop per theme, so the three grounds read apart at a glance.
        if (area.Key == "Frost")
        {
            BuildIceSpikes(field.transform, propMaterial);
        }
        else if (area.Key == "Ember")
        {
            BuildLavaPools(field.transform, propMaterial, bouldMaterial);
        }

        // The way out stands where you land, and it is the only gate in the area.
        BuildWarpPad(
            field.transform,
            "WarpPadField" + area.Key,
            new Vector3(0f, 0f, -25f),
            "마을",
            VillageArrival,
            "PortalVillage",
            new Color(0.30f, 0.80f, 0.85f)
        );

        var oreSpots = new[]
        {
            new Vector3(-12f, 0f, 12f),
            new Vector3(14f, 0f, -6f),
            new Vector3(-4f, 0f, -18f),
            new Vector3(20f, 0f, 16f),
        };
        for (int i = 0; i < oreSpots.Length; i++)
        {
            BuildGatherNode(field.transform, $"FieldOre{area.Key}{i}", oreSpots[i], GatherKind.Ore);
        }

        var spawnerObject = new GameObject("MonsterSpawner");
        spawnerObject.transform.SetParent(field.transform, false);
        var spawner = spawnerObject.AddComponent<MonsterSpawner>();
        spawner.MonsterPrefab = monsterPrefab;
        spawner.Count = 12;
        spawner.Radius = 22f;
        spawner.LevelBonus = area.Bonus;
        spawner.Theme = area.Theme;
    }

    /// <summary>Frost ground: clusters of leaning shards pushed up out of the snow.</summary>
    static void BuildIceSpikes(Transform parent, Material iceMaterial)
    {
        var spots = new[]
        {
            new Vector3(-6f, 0f, 6f),
            new Vector3(16f, 0f, 10f),
            new Vector3(-18f, 0f, -6f),
            new Vector3(2f, 0f, -10f),
            new Vector3(22f, 0f, -18f),
        };
        for (int i = 0; i < spots.Length; i++)
        {
            var cluster = new GameObject($"IceSpikes{i}");
            cluster.transform.SetParent(parent, false);
            cluster.transform.localPosition = spots[i];
            cluster.transform.localRotation = Quaternion.Euler(0f, i * 41f, 0f);

            for (int shard = 0; shard < 3; shard++)
            {
                float height = 2.4f - shard * 0.6f;
                BodyPart(
                    PrimitiveType.Cube,
                    $"Shard{shard}",
                    cluster.transform,
                    new Vector3((shard - 1) * 0.8f, height * 0.5f, shard * 0.5f),
                    new Vector3(0.55f, height, 0.55f),
                    iceMaterial,
                    Quaternion.Euler(shard * 7f, 45f, (shard - 1) * 11f)
                );
            }
        }
    }

    /// <summary>Ember ground: glowing pools sunk in the ash, with basalt spires beside them.</summary>
    static void BuildLavaPools(Transform parent, Material lavaMaterial, Material rockMaterial)
    {
        var spots = new[]
        {
            new Vector3(-7f, 0f, 7f),
            new Vector3(15f, 0f, 12f),
            new Vector3(-16f, 0f, -8f),
            new Vector3(3f, 0f, -12f),
            new Vector3(21f, 0f, -16f),
        };
        for (int i = 0; i < spots.Length; i++)
        {
            float size = 4f + (i % 3) * 1.6f;
            CreateDisc(
                $"LavaPool{i}",
                parent,
                spots[i] + new Vector3(0f, 0.03f, 0f),
                size,
                lavaMaterial
            );

            BodyPart(
                PrimitiveType.Cylinder,
                $"Spire{i}",
                parent,
                spots[i] + new Vector3(size * 0.55f, 1.8f, size * 0.35f),
                new Vector3(0.7f, 1.8f, 0.7f),
                rockMaterial,
                Quaternion.Euler(0f, i * 33f, 6f)
            );
        }
    }

    static GameObject BuildMonsterPrefab()
    {
        var root = new GameObject("Monster");

        Material bodyMaterial = CreateMaterial("MonsterBody", Color.white);
        Material eyeMaterial = CreateMaterial("MonsterEye", new Color(0.10f, 0.10f, 0.12f));
        Material boneMaterial = CreateMaterial("MonsterBone", new Color(0.90f, 0.88f, 0.80f));
        Material clawMaterial = CreateMaterial("MonsterClaw", new Color(0.20f, 0.18f, 0.20f));
        Material iceMaterial = CreateMaterial("MonsterIce", new Color(0.66f, 0.88f, 1f));
        Material emberMaterial = CreateMaterial("MonsterEmber", new Color(1f, 0.48f, 0.12f));

        // One body per kind followed by one boss per theme, all built into the same prefab.
        // Monster shows only the one that matches, which keeps a single network prefab.
        var bodies = new[]
        {
            BuildSlimeBody(root.transform, bodyMaterial, eyeMaterial),
            BuildGoblinBody(root.transform, bodyMaterial, eyeMaterial),
            BuildOrcBody(root.transform, bodyMaterial, eyeMaterial, boneMaterial),
            BuildHoundBody(root.transform, bodyMaterial, eyeMaterial, boneMaterial, iceMaterial),
            BuildWraithBody(root.transform, bodyMaterial, eyeMaterial, clawMaterial, iceMaterial),
            BuildGolemBody(root.transform, bodyMaterial, eyeMaterial, clawMaterial, iceMaterial),
            BuildToadBody(root.transform, bodyMaterial, eyeMaterial, emberMaterial),
            BuildScorpionBody(
                root.transform,
                bodyMaterial,
                eyeMaterial,
                clawMaterial,
                emberMaterial
            ),
            BuildWispBody(root.transform, bodyMaterial, eyeMaterial, emberMaterial, clawMaterial),
            BuildOgreBody(root.transform, bodyMaterial, eyeMaterial, boneMaterial, clawMaterial),
            BuildFrostGiantBody(root.transform, bodyMaterial, eyeMaterial, iceMaterial),
            BuildLavaBruteBody(
                root.transform,
                bodyMaterial,
                eyeMaterial,
                emberMaterial,
                clawMaterial
            ),
        };

        // A controller rather than a plain collider: the server moves monsters with it, so
        // they collide with walls instead of sliding through them.
        var controller = Walker(root, 2.2f, 0.5f);
        controller.slopeLimit = 55f;
        controller.stepOffset = 0.5f;

        root.AddComponent<NetworkObject>();

        var networkTransform = root.AddComponent<NetworkTransform>();
        networkTransform.Interpolate = true;
        networkTransform.SyncScaleX = false;
        networkTransform.SyncScaleY = false;
        networkTransform.SyncScaleZ = false;

        Material ringMaterial = CreateMaterial("SlamRing", new Color(1f, 0.62f, 0.30f));
        var slamRing = CreateDisc(
            "SlamRing",
            root.transform,
            new Vector3(0f, 0.06f, 0f),
            1f,
            ringMaterial
        );
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

        BodyPart(
            PrimitiveType.Sphere,
            "Blob",
            body.transform,
            new Vector3(0f, 0.5f, 0f),
            new Vector3(1.5f, 0.95f, 1.4f),
            bodyMaterial
        );
        BodyPart(
            PrimitiveType.Sphere,
            "Cap",
            body.transform,
            new Vector3(0f, 0.92f, -0.05f),
            new Vector3(0.85f, 0.5f, 0.8f),
            bodyMaterial
        );
        BodyPart(
            PrimitiveType.Sphere,
            "EyeLeft",
            body.transform,
            new Vector3(-0.28f, 0.68f, 0.6f),
            new Vector3(0.22f, 0.22f, 0.12f),
            eyeMaterial
        );
        BodyPart(
            PrimitiveType.Sphere,
            "EyeRight",
            body.transform,
            new Vector3(0.28f, 0.68f, 0.6f),
            new Vector3(0.22f, 0.22f, 0.12f),
            eyeMaterial
        );
        return body;
    }

    /// <summary>Small hunched humanoid with pointed ears and long empty arms.</summary>
    static GameObject BuildGoblinBody(Transform parent, Material bodyMaterial, Material eyeMaterial)
    {
        var body = new GameObject("GoblinBody");
        body.transform.SetParent(parent, false);

        BodyPart(
            PrimitiveType.Cube,
            "Torso",
            body.transform,
            new Vector3(0f, 0.85f, 0f),
            new Vector3(0.6f, 0.7f, 0.45f),
            bodyMaterial
        );
        BodyPart(
            PrimitiveType.Sphere,
            "Head",
            body.transform,
            new Vector3(0f, 1.4f, 0.05f),
            new Vector3(0.5f, 0.5f, 0.5f),
            bodyMaterial
        );
        BodyPart(
            PrimitiveType.Cube,
            "EarLeft",
            body.transform,
            new Vector3(-0.34f, 1.5f, 0f),
            new Vector3(0.3f, 0.12f, 0.1f),
            bodyMaterial
        );
        BodyPart(
            PrimitiveType.Cube,
            "EarRight",
            body.transform,
            new Vector3(0.34f, 1.5f, 0f),
            new Vector3(0.3f, 0.12f, 0.1f),
            bodyMaterial
        );
        BodyPart(
            PrimitiveType.Sphere,
            "EyeLeft",
            body.transform,
            new Vector3(-0.14f, 1.45f, 0.24f),
            new Vector3(0.13f, 0.13f, 0.08f),
            eyeMaterial
        );
        BodyPart(
            PrimitiveType.Sphere,
            "EyeRight",
            body.transform,
            new Vector3(0.14f, 1.45f, 0.24f),
            new Vector3(0.13f, 0.13f, 0.08f),
            eyeMaterial
        );
        // Pulled in from the torso's side so the shoulder does not float clear of it.
        BodyPart(
            PrimitiveType.Cube,
            "ArmLeft",
            body.transform,
            new Vector3(-0.36f, 0.77f, 0.08f),
            new Vector3(0.16f, 0.78f, 0.18f),
            bodyMaterial
        );
        BodyPart(
            PrimitiveType.Cube,
            "ArmRight",
            body.transform,
            new Vector3(0.36f, 0.77f, 0.08f),
            new Vector3(0.16f, 0.78f, 0.18f),
            bodyMaterial
        );
        BodyPart(
            PrimitiveType.Cube,
            "LegLeft",
            body.transform,
            new Vector3(-0.17f, 0.25f, 0f),
            new Vector3(0.2f, 0.5f, 0.22f),
            bodyMaterial
        );
        BodyPart(
            PrimitiveType.Cube,
            "LegRight",
            body.transform,
            new Vector3(0.17f, 0.25f, 0f),
            new Vector3(0.2f, 0.5f, 0.22f),
            bodyMaterial
        );
        return body;
    }

    /// <summary>Heavy brute: broad shoulders, thick arms, tusks.</summary>
    static GameObject BuildOrcBody(
        Transform parent,
        Material bodyMaterial,
        Material eyeMaterial,
        Material boneMaterial
    )
    {
        var body = new GameObject("OrcBody");
        body.transform.SetParent(parent, false);

        BodyPart(
            PrimitiveType.Cube,
            "Torso",
            body.transform,
            new Vector3(0f, 1.15f, 0f),
            new Vector3(1.05f, 0.95f, 0.65f),
            bodyMaterial
        );
        BodyPart(
            PrimitiveType.Cube,
            "Shoulders",
            body.transform,
            new Vector3(0f, 1.6f, 0f),
            new Vector3(1.5f, 0.32f, 0.7f),
            bodyMaterial
        );
        BodyPart(
            PrimitiveType.Sphere,
            "Head",
            body.transform,
            new Vector3(0f, 1.95f, 0.08f),
            new Vector3(0.62f, 0.6f, 0.62f),
            bodyMaterial
        );
        BodyPart(
            PrimitiveType.Cube,
            "TuskLeft",
            body.transform,
            new Vector3(-0.16f, 1.82f, 0.3f),
            new Vector3(0.09f, 0.26f, 0.09f),
            boneMaterial
        );
        BodyPart(
            PrimitiveType.Cube,
            "TuskRight",
            body.transform,
            new Vector3(0.16f, 1.82f, 0.3f),
            new Vector3(0.09f, 0.26f, 0.09f),
            boneMaterial
        );
        BodyPart(
            PrimitiveType.Sphere,
            "EyeLeft",
            body.transform,
            new Vector3(-0.17f, 2.02f, 0.3f),
            new Vector3(0.14f, 0.14f, 0.08f),
            eyeMaterial
        );
        BodyPart(
            PrimitiveType.Sphere,
            "EyeRight",
            body.transform,
            new Vector3(0.17f, 2.02f, 0.3f),
            new Vector3(0.14f, 0.14f, 0.08f),
            eyeMaterial
        );
        BodyPart(
            PrimitiveType.Cube,
            "ArmLeft",
            body.transform,
            new Vector3(-0.78f, 1.15f, 0.05f),
            new Vector3(0.3f, 0.95f, 0.32f),
            bodyMaterial
        );
        BodyPart(
            PrimitiveType.Cube,
            "ArmRight",
            body.transform,
            new Vector3(0.78f, 1.15f, 0.05f),
            new Vector3(0.3f, 0.95f, 0.32f),
            bodyMaterial
        );
        BodyPart(
            PrimitiveType.Cube,
            "LegLeft",
            body.transform,
            new Vector3(-0.3f, 0.35f, 0f),
            new Vector3(0.34f, 0.7f, 0.36f),
            bodyMaterial
        );
        BodyPart(
            PrimitiveType.Cube,
            "LegRight",
            body.transform,
            new Vector3(0.3f, 0.35f, 0f),
            new Vector3(0.34f, 0.7f, 0.36f),
            bodyMaterial
        );
        return body;
    }

    /// <summary>
    /// Frost ground: a lean four-legged hunter. Ruffed neck, frost along the spine, paws
    /// under every leg so the step lands on something.
    /// </summary>
    static GameObject BuildHoundBody(
        Transform parent,
        Material bodyMaterial,
        Material eyeMaterial,
        Material boneMaterial,
        Material iceMaterial
    )
    {
        var body = new GameObject("HoundBody");
        body.transform.SetParent(parent, false);

        BodyPart(
            PrimitiveType.Cube,
            "Torso",
            body.transform,
            new Vector3(0f, 0.72f, -0.15f),
            new Vector3(0.5f, 0.46f, 1f),
            bodyMaterial
        );
        BodyPart(
            PrimitiveType.Cube,
            "Haunch",
            body.transform,
            new Vector3(0f, 0.76f, -0.6f),
            new Vector3(0.56f, 0.52f, 0.42f),
            bodyMaterial
        );
        BodyPart(
            PrimitiveType.Cube,
            "Chest",
            body.transform,
            new Vector3(0f, 0.76f, 0.4f),
            new Vector3(0.6f, 0.56f, 0.42f),
            bodyMaterial
        );
        BodyPart(
            PrimitiveType.Cube,
            "Neck",
            body.transform,
            new Vector3(0f, 0.88f, 0.62f),
            new Vector3(0.4f, 0.36f, 0.3f),
            bodyMaterial
        );
        BodyPart(
            PrimitiveType.Sphere,
            "Head",
            body.transform,
            new Vector3(0f, 0.99f, 0.8f),
            new Vector3(0.42f, 0.4f, 0.48f),
            bodyMaterial
        );
        BodyPart(
            PrimitiveType.Cube,
            "Snout",
            body.transform,
            new Vector3(0f, 0.9f, 1.08f),
            new Vector3(0.22f, 0.19f, 0.34f),
            bodyMaterial
        );
        BodyPart(
            PrimitiveType.Cube,
            "Jaw",
            body.transform,
            new Vector3(0f, 0.79f, 1.04f),
            new Vector3(0.2f, 0.12f, 0.3f),
            bodyMaterial
        );
        BodyPart(
            PrimitiveType.Cube,
            "Brow",
            body.transform,
            new Vector3(0f, 1.12f, 0.92f),
            new Vector3(0.34f, 0.1f, 0.2f),
            bodyMaterial
        );

        // Ruff around the neck, three overlapping tufts.
        for (int i = 0; i < 3; i++)
        {
            BodyPart(
                PrimitiveType.Cube,
                $"Ruff{i}",
                body.transform,
                new Vector3((i - 1) * 0.25f, 0.92f, 0.5f),
                new Vector3(0.3f, 0.42f, 0.3f),
                bodyMaterial,
                Quaternion.Euler(0f, 0f, (i - 1) * 24f)
            );
        }

        // Frost growing out of the back, tallest over the shoulders.
        for (int i = 0; i < 4; i++)
        {
            BodyPart(
                PrimitiveType.Cube,
                $"DetailSpine{i}",
                body.transform,
                new Vector3(0f, 0.98f, 0.2f - i * 0.34f),
                new Vector3(0.12f, 0.42f - i * 0.06f, 0.12f),
                iceMaterial,
                Quaternion.Euler(-18f - i * 5f, 45f, 0f)
            );
        }

        foreach (float side in new[] { -1f, 1f })
        {
            string suffix = side < 0f ? "Left" : "Right";

            BodyPart(
                PrimitiveType.Cube,
                "Ear" + suffix,
                body.transform,
                new Vector3(side * 0.16f, 1.22f, 0.72f),
                new Vector3(0.1f, 0.26f, 0.1f),
                bodyMaterial,
                Quaternion.Euler(-14f, 0f, side * 14f)
            );

            BodyPart(
                PrimitiveType.Sphere,
                "Eye" + suffix,
                body.transform,
                new Vector3(side * 0.14f, 1.03f, 1f),
                new Vector3(0.1f, 0.1f, 0.06f),
                iceMaterial
            );
            BodyPart(
                PrimitiveType.Cube,
                "Tusk" + suffix,
                body.transform,
                new Vector3(side * 0.07f, 0.8f, 1.2f),
                new Vector3(0.05f, 0.13f, 0.05f),
                boneMaterial
            );

            BodyPart(
                PrimitiveType.Cube,
                "LegFront" + suffix,
                body.transform,
                new Vector3(side * 0.2f, 0.34f, 0.44f),
                new Vector3(0.16f, 0.6f, 0.18f),
                bodyMaterial
            );
            BodyPart(
                PrimitiveType.Cube,
                "LegBack" + suffix,
                body.transform,
                new Vector3(side * 0.22f, 0.34f, -0.52f),
                new Vector3(0.18f, 0.6f, 0.2f),
                bodyMaterial
            );
            BodyPart(
                PrimitiveType.Cube,
                "PawFront" + suffix,
                body.transform,
                new Vector3(side * 0.2f, 0.06f, 0.5f),
                new Vector3(0.2f, 0.12f, 0.28f),
                bodyMaterial
            );
            BodyPart(
                PrimitiveType.Cube,
                "PawBack" + suffix,
                body.transform,
                new Vector3(side * 0.22f, 0.06f, -0.5f),
                new Vector3(0.22f, 0.12f, 0.28f),
                bodyMaterial
            );
        }

        BodyPart(
            PrimitiveType.Cube,
            "Tail",
            body.transform,
            new Vector3(0f, 0.88f, -0.84f),
            new Vector3(0.15f, 0.15f, 0.5f),
            bodyMaterial,
            Quaternion.Euler(-26f, 0f, 0f)
        );
        BodyPart(
            PrimitiveType.Cube,
            "DetailTailTip",
            body.transform,
            new Vector3(0f, 1.04f, -1.1f),
            new Vector3(0.14f, 0.14f, 0.22f),
            iceMaterial
        );
        return body;
    }

    /// <summary>
    /// Frost ground: a hooded thing that hangs above the ground. Crown of ice on the hood,
    /// a frozen chain at the waist, and a hem that hangs in tatters.
    /// </summary>
    static GameObject BuildWraithBody(
        Transform parent,
        Material bodyMaterial,
        Material eyeMaterial,
        Material clawMaterial,
        Material iceMaterial
    )
    {
        var body = new GameObject("WraithBody");
        body.transform.SetParent(parent, false);

        BodyPart(
            PrimitiveType.Sphere,
            "Skirt",
            body.transform,
            new Vector3(0f, 0.45f, 0f),
            new Vector3(1.15f, 0.7f, 1.05f),
            bodyMaterial
        );
        BodyPart(
            PrimitiveType.Sphere,
            "Robe",
            body.transform,
            new Vector3(0f, 1.05f, 0f),
            new Vector3(0.9f, 1.25f, 0.85f),
            bodyMaterial
        );
        BodyPart(
            PrimitiveType.Cube,
            "DetailChain",
            body.transform,
            new Vector3(0f, 0.9f, 0f),
            new Vector3(0.98f, 0.13f, 0.92f),
            iceMaterial
        );
        BodyPart(
            PrimitiveType.Cube,
            "Mantle",
            body.transform,
            new Vector3(0f, 1.42f, -0.04f),
            new Vector3(1.05f, 0.3f, 0.9f),
            bodyMaterial
        );
        BodyPart(
            PrimitiveType.Sphere,
            "Hood",
            body.transform,
            new Vector3(0f, 1.78f, 0.02f),
            new Vector3(0.74f, 0.78f, 0.74f),
            bodyMaterial
        );
        BodyPart(
            PrimitiveType.Cube,
            "Brim",
            body.transform,
            new Vector3(0f, 1.6f, 0.12f),
            new Vector3(0.68f, 0.12f, 0.68f),
            bodyMaterial
        );
        BodyPart(
            PrimitiveType.Cube,
            "EyeHollow",
            body.transform,
            new Vector3(0f, 1.72f, 0.32f),
            new Vector3(0.44f, 0.3f, 0.14f),
            eyeMaterial
        );

        // A crown of ice around the back of the hood.
        for (int i = 0; i < 5; i++)
        {
            float angle = (i * 40f + 110f) * Mathf.Deg2Rad;
            BodyPart(
                PrimitiveType.Cube,
                $"DetailCrown{i}",
                body.transform,
                new Vector3(Mathf.Cos(angle) * 0.36f, 2.08f, Mathf.Sin(angle) * 0.34f - 0.05f),
                new Vector3(0.12f, 0.42f - Mathf.Abs(i - 2) * 0.06f, 0.12f),
                iceMaterial,
                Quaternion.Euler(-Mathf.Sin(angle) * 24f, 45f, Mathf.Cos(angle) * 24f)
            );
        }

        foreach (float side in new[] { -1f, 1f })
        {
            string suffix = side < 0f ? "Left" : "Right";
            BodyPart(
                PrimitiveType.Sphere,
                "EyeGlow" + suffix,
                body.transform,
                new Vector3(side * 0.13f, 1.74f, 0.4f),
                new Vector3(0.12f, 0.12f, 0.07f),
                iceMaterial
            );

            BodyPart(
                PrimitiveType.Cube,
                "Sleeve" + suffix,
                body.transform,
                new Vector3(side * 0.54f, 1f, 0.1f),
                new Vector3(0.26f, 1f, 0.28f),
                bodyMaterial,
                Quaternion.Euler(0f, 0f, side * 13f)
            );
        }

        // Ragged hem: alternating lengths, so the bottom never reads as a solid ball.
        for (int i = 0; i < 7; i++)
        {
            float angle = i * 51f * Mathf.Deg2Rad;
            BodyPart(
                PrimitiveType.Cube,
                $"Rag{i}",
                body.transform,
                new Vector3(
                    Mathf.Cos(angle) * 0.46f,
                    0.18f - (i % 2) * 0.08f,
                    Mathf.Sin(angle) * 0.43f
                ),
                new Vector3(0.2f, 0.44f + (i % 2) * 0.18f, 0.2f),
                bodyMaterial,
                Quaternion.Euler((i % 2) * 8f, i * 24f, (i % 3 - 1) * 9f)
            );
        }

        return body;
    }

    /// <summary>
    /// Frost ground: a slab of walking ice. Plated shoulders, a split crystal on the back,
    /// spikes over the knuckles and a seam glowing down the chest.
    /// </summary>
    static GameObject BuildGolemBody(
        Transform parent,
        Material bodyMaterial,
        Material eyeMaterial,
        Material clawMaterial,
        Material iceMaterial
    )
    {
        var body = new GameObject("GolemBody");
        body.transform.SetParent(parent, false);

        BodyPart(
            PrimitiveType.Cube,
            "Hips",
            body.transform,
            new Vector3(0f, 0.68f, 0f),
            new Vector3(0.95f, 0.55f, 0.72f),
            bodyMaterial
        );
        BodyPart(
            PrimitiveType.Cube,
            "Torso",
            body.transform,
            new Vector3(0f, 1.35f, 0f),
            new Vector3(1.25f, 1.1f, 0.88f),
            bodyMaterial
        );
        BodyPart(
            PrimitiveType.Cube,
            "Shoulders",
            body.transform,
            new Vector3(0f, 1.88f, 0f),
            new Vector3(1.75f, 0.42f, 0.95f),
            bodyMaterial
        );
        BodyPart(
            PrimitiveType.Cube,
            "Head",
            body.transform,
            new Vector3(0f, 2.15f, 0.06f),
            new Vector3(0.5f, 0.46f, 0.5f),
            bodyMaterial
        );
        BodyPart(
            PrimitiveType.Cube,
            "Brow",
            body.transform,
            new Vector3(0f, 2.3f, 0.16f),
            new Vector3(0.56f, 0.14f, 0.34f),
            bodyMaterial
        );
        BodyPart(
            PrimitiveType.Cube,
            "DetailSeam",
            body.transform,
            new Vector3(0f, 1.35f, 0.45f),
            new Vector3(0.14f, 0.72f, 0.06f),
            iceMaterial
        );
        BodyPart(
            PrimitiveType.Cube,
            "DetailSeamCross",
            body.transform,
            new Vector3(0f, 1.55f, 0.45f),
            new Vector3(0.5f, 0.1f, 0.06f),
            iceMaterial
        );
        BodyPart(
            PrimitiveType.Cube,
            "DetailCrack",
            body.transform,
            new Vector3(0f, 0.7f, 0.38f),
            new Vector3(0.34f, 0.08f, 0.06f),
            clawMaterial
        );

        // The crystal on its back, one tall shard flanked by two short ones.
        for (int i = 0; i < 3; i++)
        {
            BodyPart(
                PrimitiveType.Cube,
                $"DetailCrystal{i}",
                body.transform,
                new Vector3((i - 1) * 0.32f, 2.0f - Mathf.Abs(i - 1) * 0.24f, -0.52f),
                new Vector3(0.24f, 0.72f - Mathf.Abs(i - 1) * 0.22f, 0.24f),
                iceMaterial,
                Quaternion.Euler(22f, 45f, (i - 1) * 16f)
            );
        }

        foreach (float side in new[] { -1f, 1f })
        {
            string suffix = side < 0f ? "Left" : "Right";
            BodyPart(
                PrimitiveType.Sphere,
                "Eye" + suffix,
                body.transform,
                new Vector3(side * 0.14f, 2.15f, 0.28f),
                new Vector3(0.12f, 0.12f, 0.06f),
                iceMaterial
            );

            BodyPart(
                PrimitiveType.Cube,
                "Arm" + suffix,
                body.transform,
                new Vector3(side * 0.92f, 1.12f, 0f),
                new Vector3(0.4f, 1.5f, 0.42f),
                bodyMaterial
            );
            BodyPart(
                PrimitiveType.Cube,
                "Leg" + suffix,
                body.transform,
                new Vector3(side * 0.32f, 0.32f, 0f),
                new Vector3(0.42f, 0.64f, 0.46f),
                bodyMaterial
            );
            BodyPart(
                PrimitiveType.Cube,
                "Foot" + suffix,
                body.transform,
                new Vector3(side * 0.32f, 0.06f, 0.08f),
                new Vector3(0.48f, 0.14f, 0.6f),
                bodyMaterial
            );

            BodyPart(
                PrimitiveType.Cube,
                "ShoulderShard" + suffix,
                body.transform,
                new Vector3(side * 0.72f, 2.12f, 0f),
                new Vector3(0.32f, 0.36f, 0.32f),
                bodyMaterial,
                Quaternion.Euler(0f, 45f, side * 18f)
            );
            BodyPart(
                PrimitiveType.Cube,
                "DetailShoulderSpike" + suffix,
                body.transform,
                new Vector3(side * 0.8f, 2.42f, 0f),
                new Vector3(0.16f, 0.44f, 0.16f),
                iceMaterial,
                Quaternion.Euler(0f, 45f, side * 26f)
            );
        }

        return body;
    }

    /// <summary>
    /// Lava ground: a squat toad. No limbs are named as such on purpose - it has nothing to
    /// step with, so the walk motion hops the whole body instead, which is how a toad moves.
    /// </summary>
    static GameObject BuildToadBody(
        Transform parent,
        Material bodyMaterial,
        Material eyeMaterial,
        Material emberMaterial
    )
    {
        var body = new GameObject("ToadBody");
        body.transform.SetParent(parent, false);

        BodyPart(
            PrimitiveType.Sphere,
            "Belly",
            body.transform,
            new Vector3(0f, 0.42f, 0f),
            new Vector3(1.05f, 0.66f, 1.05f),
            bodyMaterial
        );
        BodyPart(
            PrimitiveType.Sphere,
            "Back",
            body.transform,
            new Vector3(0f, 0.6f, -0.1f),
            new Vector3(0.9f, 0.52f, 0.92f),
            bodyMaterial
        );
        BodyPart(
            PrimitiveType.Sphere,
            "Head",
            body.transform,
            new Vector3(0f, 0.52f, 0.42f),
            new Vector3(0.82f, 0.5f, 0.66f),
            bodyMaterial
        );
        BodyPart(
            PrimitiveType.Sphere,
            "DetailThroat",
            body.transform,
            new Vector3(0f, 0.3f, 0.44f),
            new Vector3(0.56f, 0.36f, 0.44f),
            emberMaterial
        );
        BodyPart(
            PrimitiveType.Cube,
            "DetailMouth",
            body.transform,
            new Vector3(0f, 0.4f, 0.68f),
            new Vector3(0.62f, 0.11f, 0.16f),
            emberMaterial
        );

        // Warts across the back, hot enough to show.
        for (int i = 0; i < 5; i++)
        {
            float angle = i * 72f * Mathf.Deg2Rad;
            BodyPart(
                PrimitiveType.Sphere,
                $"DetailWart{i}",
                body.transform,
                new Vector3(
                    Mathf.Cos(angle) * 0.32f,
                    0.8f - (i % 2) * 0.08f,
                    Mathf.Sin(angle) * 0.34f - 0.08f
                ),
                new Vector3(0.18f, 0.14f, 0.18f),
                emberMaterial
            );
        }

        foreach (float side in new[] { -1f, 1f })
        {
            string suffix = side < 0f ? "Left" : "Right";

            // Bulging eye on top of the head, dark pupil sunk into it.
            BodyPart(
                PrimitiveType.Sphere,
                "Brow" + suffix,
                body.transform,
                new Vector3(side * 0.25f, 0.76f, 0.32f),
                new Vector3(0.3f, 0.3f, 0.3f),
                bodyMaterial
            );
            BodyPart(
                PrimitiveType.Sphere,
                "Eye" + suffix,
                body.transform,
                new Vector3(side * 0.27f, 0.8f, 0.44f),
                new Vector3(0.16f, 0.16f, 0.1f),
                eyeMaterial
            );

            // Hind leg folded up against the body, front foot planted underneath.
            BodyPart(
                PrimitiveType.Sphere,
                "Haunch" + suffix,
                body.transform,
                new Vector3(side * 0.46f, 0.38f, -0.22f),
                new Vector3(0.44f, 0.52f, 0.66f),
                bodyMaterial,
                Quaternion.Euler(0f, side * 14f, 0f)
            );
            BodyPart(
                PrimitiveType.Cube,
                "PawBack" + suffix,
                body.transform,
                new Vector3(side * 0.52f, 0.09f, 0.02f),
                new Vector3(0.3f, 0.18f, 0.56f),
                bodyMaterial
            );
            BodyPart(
                PrimitiveType.Cube,
                "PawFront" + suffix,
                body.transform,
                new Vector3(side * 0.3f, 0.09f, 0.46f),
                new Vector3(0.28f, 0.18f, 0.38f),
                bodyMaterial
            );
        }

        return body;
    }

    /// <summary>
    /// Lava ground: a plated crawler. Segmented shell with the glow showing between the
    /// plates, pincers out front, and a tail that arcs over its back.
    /// </summary>
    static GameObject BuildScorpionBody(
        Transform parent,
        Material bodyMaterial,
        Material eyeMaterial,
        Material clawMaterial,
        Material emberMaterial
    )
    {
        var body = new GameObject("ScorpionBody");
        body.transform.SetParent(parent, false);

        BodyPart(
            PrimitiveType.Cube,
            "Torso",
            body.transform,
            new Vector3(0f, 0.5f, -0.15f),
            new Vector3(0.85f, 0.4f, 1f),
            bodyMaterial
        );
        BodyPart(
            PrimitiveType.Cube,
            "Head",
            body.transform,
            new Vector3(0f, 0.52f, 0.5f),
            new Vector3(0.62f, 0.34f, 0.45f),
            bodyMaterial
        );
        BodyPart(
            PrimitiveType.Cube,
            "MandibleLeft",
            body.transform,
            new Vector3(-0.14f, 0.44f, 0.76f),
            new Vector3(0.1f, 0.12f, 0.22f),
            clawMaterial
        );
        BodyPart(
            PrimitiveType.Cube,
            "MandibleRight",
            body.transform,
            new Vector3(0.14f, 0.44f, 0.76f),
            new Vector3(0.1f, 0.12f, 0.22f),
            clawMaterial
        );

        // Shell plates down the back, with the heat showing in the gaps.
        for (int i = 0; i < 3; i++)
        {
            BodyPart(
                PrimitiveType.Cube,
                $"Plate{i}",
                body.transform,
                new Vector3(0f, 0.72f, 0.2f - i * 0.36f),
                new Vector3(0.78f - i * 0.06f, 0.2f, 0.3f),
                bodyMaterial
            );
            BodyPart(
                PrimitiveType.Cube,
                $"DetailGlow{i}",
                body.transform,
                new Vector3(0f, 0.68f, 0.02f - i * 0.36f),
                new Vector3(0.8f - i * 0.06f, 0.1f, 0.07f),
                emberMaterial
            );
        }

        foreach (float side in new[] { -1f, 1f })
        {
            string suffix = side < 0f ? "Left" : "Right";

            BodyPart(
                PrimitiveType.Sphere,
                "Eye" + suffix,
                body.transform,
                new Vector3(side * 0.15f, 0.63f, 0.7f),
                new Vector3(0.1f, 0.1f, 0.06f),
                emberMaterial
            );
            BodyPart(
                PrimitiveType.Sphere,
                "EyeSmall" + suffix,
                body.transform,
                new Vector3(side * 0.26f, 0.6f, 0.62f),
                new Vector3(0.07f, 0.07f, 0.05f),
                eyeMaterial
            );

            // Pincer: an arm reaching forward, a claw split in two, and a spur on the elbow.
            BodyPart(
                PrimitiveType.Cube,
                "Arm" + suffix,
                body.transform,
                new Vector3(side * 0.52f, 0.46f, 0.62f),
                new Vector3(0.2f, 0.2f, 0.62f),
                bodyMaterial,
                Quaternion.Euler(0f, side * 18f, 0f)
            );
            BodyPart(
                PrimitiveType.Cube,
                "ClawUpper" + suffix,
                body.transform,
                new Vector3(side * 0.68f, 0.57f, 1.02f),
                new Vector3(0.17f, 0.15f, 0.44f),
                clawMaterial
            );
            BodyPart(
                PrimitiveType.Cube,
                "ClawLower" + suffix,
                body.transform,
                new Vector3(side * 0.68f, 0.38f, 0.98f),
                new Vector3(0.17f, 0.15f, 0.38f),
                clawMaterial
            );
            BodyPart(
                PrimitiveType.Cube,
                "ClawSpur" + suffix,
                body.transform,
                new Vector3(side * 0.5f, 0.6f, 0.4f),
                new Vector3(0.1f, 0.24f, 0.1f),
                clawMaterial
            );

            for (int leg = 0; leg < 3; leg++)
            {
                BodyPart(
                    PrimitiveType.Cube,
                    $"Leg{suffix}{leg}",
                    body.transform,
                    new Vector3(side * 0.55f, 0.3f, 0.15f - leg * 0.42f),
                    new Vector3(0.42f, 0.12f, 0.12f),
                    bodyMaterial,
                    Quaternion.Euler(0f, 0f, side * -24f)
                );

                // Where the leg segment above actually ends, so the foot meets it instead of
                // floating off to the side.
                BodyPart(
                    PrimitiveType.Cube,
                    $"ClawTip{suffix}{leg}",
                    body.transform,
                    new Vector3(side * 0.65f, 0.07f, 0.15f - leg * 0.42f),
                    new Vector3(0.1f, 0.4f, 0.1f),
                    clawMaterial,
                    Quaternion.Euler(0f, 0f, side * -16f)
                );
            }
        }

        // Tail: segments arcing up over the back, ending in a stinger.
        for (int i = 0; i < 5; i++)
        {
            float t = i / 4f;
            BodyPart(
                PrimitiveType.Cube,
                $"Tail{i}",
                body.transform,
                new Vector3(0f, 0.55f + t * 0.95f, -0.75f - t * 0.3f + t * t * 0.55f),
                new Vector3(0.3f - t * 0.09f, 0.28f - t * 0.07f, 0.32f),
                bodyMaterial,
                Quaternion.Euler(-28f - i * 16f, 0f, 0f)
            );
            BodyPart(
                PrimitiveType.Cube,
                $"DetailTailGlow{i}",
                body.transform,
                new Vector3(0f, 0.55f + t * 0.95f, -0.62f - t * 0.3f + t * t * 0.55f),
                new Vector3(0.14f - t * 0.03f, 0.08f, 0.06f),
                emberMaterial
            );
        }

        BodyPart(
            PrimitiveType.Cube,
            "ClawStinger",
            body.transform,
            new Vector3(0f, 1.6f, -0.42f),
            new Vector3(0.2f, 0.4f, 0.2f),
            clawMaterial,
            Quaternion.Euler(35f, 45f, 0f)
        );
        BodyPart(
            PrimitiveType.Cube,
            "DetailVenom",
            body.transform,
            new Vector3(0f, 1.4f, -0.3f),
            new Vector3(0.12f, 0.16f, 0.12f),
            emberMaterial
        );
        return body;
    }

    /// <summary>
    /// Lava ground: a burning core inside a broken crust, shards held in orbit around it
    /// and flame streaming off the top.
    /// </summary>
    static GameObject BuildWispBody(
        Transform parent,
        Material bodyMaterial,
        Material eyeMaterial,
        Material emberMaterial,
        Material clawMaterial
    )
    {
        var body = new GameObject("WispBody");
        body.transform.SetParent(parent, false);

        BodyPart(
            PrimitiveType.Sphere,
            "Core",
            body.transform,
            new Vector3(0f, 1.15f, 0f),
            new Vector3(0.8f, 0.9f, 0.8f),
            emberMaterial
        );

        // Crust: broken plates wrapped around the core, leaving the glow showing through.
        var plates = new (Vector3 position, Vector3 scale, Vector3 rotation)[]
        {
            (
                new Vector3(0f, 1.5f, -0.06f),
                new Vector3(0.72f, 0.4f, 0.72f),
                new Vector3(0f, 20f, 0f)
            ),
            (
                new Vector3(-0.3f, 1.05f, -0.24f),
                new Vector3(0.5f, 0.7f, 0.5f),
                new Vector3(0f, 35f, 14f)
            ),
            (
                new Vector3(0.32f, 1.0f, -0.2f),
                new Vector3(0.48f, 0.66f, 0.48f),
                new Vector3(0f, -30f, -12f)
            ),
            (
                new Vector3(0f, 0.78f, 0.06f),
                new Vector3(0.66f, 0.34f, 0.62f),
                new Vector3(8f, 15f, 0f)
            ),
        };
        for (int i = 0; i < plates.Length; i++)
        {
            BodyPart(
                PrimitiveType.Cube,
                $"DetailCrust{i}",
                body.transform,
                plates[i].position,
                plates[i].scale,
                clawMaterial,
                Quaternion.Euler(plates[i].rotation)
            );
        }

        BodyPart(
            PrimitiveType.Sphere,
            "EyeLeft",
            body.transform,
            new Vector3(-0.17f, 1.34f, 0.33f),
            new Vector3(0.13f, 0.13f, 0.08f),
            eyeMaterial
        );
        BodyPart(
            PrimitiveType.Sphere,
            "EyeRight",
            body.transform,
            new Vector3(0.17f, 1.34f, 0.33f),
            new Vector3(0.13f, 0.13f, 0.08f),
            eyeMaterial
        );
        BodyPart(
            PrimitiveType.Cube,
            "DetailMouth",
            body.transform,
            new Vector3(0f, 1.12f, 0.36f),
            new Vector3(0.3f, 0.09f, 0.06f),
            emberMaterial
        );

        // Shards held in orbit, and cinders drifting between them.
        for (int i = 0; i < 6; i++)
        {
            float angle = i * 60f * Mathf.Deg2Rad;
            BodyPart(
                PrimitiveType.Cube,
                $"Shard{i}",
                body.transform,
                new Vector3(
                    Mathf.Cos(angle) * 0.72f,
                    0.95f + (i % 3) * 0.3f,
                    Mathf.Sin(angle) * 0.72f
                ),
                new Vector3(0.22f, 0.36f - (i % 3) * 0.06f, 0.22f),
                bodyMaterial,
                Quaternion.Euler(i * 19f, 45f, 22f + i * 7f)
            );

            BodyPart(
                PrimitiveType.Cube,
                $"DetailCinder{i}",
                body.transform,
                new Vector3(
                    Mathf.Cos(angle + 0.5f) * 0.5f,
                    0.7f + (i % 2) * 0.9f,
                    Mathf.Sin(angle + 0.5f) * 0.5f
                ),
                new Vector3(0.1f, 0.1f, 0.1f),
                emberMaterial
            );
        }

        // Flame off the top, tallest in the middle.
        for (int i = 0; i < 5; i++)
        {
            float offset = (i - 2) * 0.16f;
            BodyPart(
                PrimitiveType.Cube,
                $"DetailFlame{i}",
                body.transform,
                new Vector3(
                    offset,
                    1.78f + (2 - Mathf.Abs(i - 2)) * 0.14f,
                    -Mathf.Abs(offset) * 0.4f
                ),
                new Vector3(0.17f, 0.44f + (2 - Mathf.Abs(i - 2)) * 0.22f, 0.17f),
                emberMaterial,
                Quaternion.Euler(0f, 45f, offset * 40f)
            );
        }

        BodyPart(
            PrimitiveType.Cube,
            "DetailAnchor",
            body.transform,
            new Vector3(0f, 0.36f, 0f),
            new Vector3(0.34f, 0.3f, 0.34f),
            clawMaterial
        );
        return body;
    }

    /// <summary>
    /// The first dungeon boss: hunched, horned, tusked and plated at the shoulders. It fights
    /// bare-handed, like everything else in the world.
    /// </summary>
    static GameObject BuildOgreBody(
        Transform parent,
        Material bodyMaterial,
        Material eyeMaterial,
        Material boneMaterial,
        Material clawMaterial
    )
    {
        var body = new GameObject("OgreBody");
        body.transform.SetParent(parent, false);

        BodyPart(
            PrimitiveType.Cube,
            "Torso",
            body.transform,
            new Vector3(0f, 1.4f, 0f),
            new Vector3(1.5f, 1.3f, 0.9f),
            bodyMaterial
        );

        // The bulk goes across rather than behind: a slab wider than the torso and wider than
        // the arms hang from reads as shoulders, where a ball the size of the head behind the
        // head only ever reads as a second head.
        BodyPart(
            PrimitiveType.Cube,
            "Shoulders",
            body.transform,
            new Vector3(0f, 2.0f, -0.02f),
            new Vector3(2.15f, 0.44f, 1f),
            bodyMaterial
        );
        BodyPart(
            PrimitiveType.Cube,
            "Neck",
            body.transform,
            new Vector3(0f, 2.2f, 0.08f),
            new Vector3(0.44f, 0.3f, 0.44f),
            bodyMaterial
        );
        BodyPart(
            PrimitiveType.Cube,
            "DetailBelt",
            body.transform,
            new Vector3(0f, 0.85f, 0f),
            new Vector3(1.55f, 0.22f, 0.95f),
            clawMaterial
        );
        BodyPart(
            PrimitiveType.Cube,
            "DetailBuckle",
            body.transform,
            new Vector3(0f, 0.85f, 0.48f),
            new Vector3(0.3f, 0.3f, 0.08f),
            boneMaterial
        );

        BodyPart(
            PrimitiveType.Sphere,
            "Head",
            body.transform,
            new Vector3(0f, 2.45f, 0.12f),
            new Vector3(0.8f, 0.78f, 0.8f),
            bodyMaterial
        );
        BodyPart(
            PrimitiveType.Cube,
            "Brow",
            body.transform,
            new Vector3(0f, 2.66f, 0.3f),
            new Vector3(0.66f, 0.16f, 0.28f),
            bodyMaterial
        );
        BodyPart(
            PrimitiveType.Cube,
            "Jaw",
            body.transform,
            new Vector3(0f, 2.24f, 0.28f),
            new Vector3(0.58f, 0.24f, 0.5f),
            bodyMaterial
        );

        foreach (float side in new[] { -1f, 1f })
        {
            string suffix = side < 0f ? "Left" : "Right";

            // Two segments per horn, the tip swept back.
            BodyPart(
                PrimitiveType.Cube,
                "Horn" + suffix,
                body.transform,
                new Vector3(side * 0.34f, 2.85f, 0.05f),
                new Vector3(0.16f, 0.46f, 0.16f),
                boneMaterial
            );
            BodyPart(
                PrimitiveType.Cube,
                "HornTip" + suffix,
                body.transform,
                new Vector3(side * 0.44f, 3.12f, -0.1f),
                new Vector3(0.12f, 0.34f, 0.12f),
                boneMaterial,
                Quaternion.Euler(-28f, 0f, side * 32f)
            );

            BodyPart(
                PrimitiveType.Cube,
                "Tusk" + suffix,
                body.transform,
                new Vector3(side * 0.2f, 2.32f, 0.42f),
                new Vector3(0.1f, 0.3f, 0.1f),
                boneMaterial
            );
            BodyPart(
                PrimitiveType.Sphere,
                "Eye" + suffix,
                body.transform,
                new Vector3(side * 0.22f, 2.52f, 0.38f),
                new Vector3(0.18f, 0.18f, 0.1f),
                eyeMaterial
            );

            // Capping the shoulder slab rather than floating beside it.
            BodyPart(
                PrimitiveType.Cube,
                "DetailPlate" + suffix,
                body.transform,
                new Vector3(side * 0.86f, 2.24f, 0f),
                new Vector3(0.66f, 0.28f, 0.78f),
                boneMaterial,
                Quaternion.Euler(0f, 0f, side * 16f)
            );

            BodyPart(
                PrimitiveType.Cube,
                "Arm" + suffix,
                body.transform,
                new Vector3(side * 1.05f, 1.25f, 0.05f),
                new Vector3(0.42f, 1.6f, 0.44f),
                bodyMaterial
            );
            BodyPart(
                PrimitiveType.Cube,
                "Leg" + suffix,
                body.transform,
                new Vector3(side * 0.42f, 0.4f, 0f),
                new Vector3(0.46f, 0.8f, 0.48f),
                bodyMaterial
            );
        }

        return body;
    }

    /// <summary>The frost dungeon boss: tall, crowned with ice, shards down its spine.</summary>
    static GameObject BuildFrostGiantBody(
        Transform parent,
        Material bodyMaterial,
        Material eyeMaterial,
        Material iceMaterial
    )
    {
        var body = new GameObject("FrostGiantBody");
        body.transform.SetParent(parent, false);

        BodyPart(
            PrimitiveType.Cube,
            "Torso",
            body.transform,
            new Vector3(0f, 1.75f, 0f),
            new Vector3(1.5f, 1.5f, 0.9f),
            bodyMaterial
        );
        BodyPart(
            PrimitiveType.Cube,
            "Hips",
            body.transform,
            new Vector3(0f, 0.95f, 0f),
            new Vector3(1.1f, 0.55f, 0.8f),
            bodyMaterial
        );
        BodyPart(
            PrimitiveType.Sphere,
            "Head",
            body.transform,
            new Vector3(0f, 2.9f, 0.1f),
            new Vector3(0.85f, 0.82f, 0.85f),
            bodyMaterial
        );
        BodyPart(
            PrimitiveType.Cube,
            "DetailBeard",
            body.transform,
            new Vector3(0f, 2.62f, 0.42f),
            new Vector3(0.5f, 0.25f, 0.28f),
            iceMaterial
        );

        // Crown of shards, and a ridge of them down the back.
        for (int i = 0; i < 5; i++)
        {
            float angle = (i * 72f - 90f) * Mathf.Deg2Rad;
            BodyPart(
                PrimitiveType.Cube,
                $"DetailCrown{i}",
                body.transform,
                new Vector3(Mathf.Cos(angle) * 0.36f, 3.35f, Mathf.Sin(angle) * 0.36f),
                new Vector3(0.16f, 0.55f, 0.16f),
                iceMaterial,
                Quaternion.Euler(Mathf.Sin(angle) * 22f, 45f, -Mathf.Cos(angle) * 22f)
            );
        }

        for (int i = 0; i < 4; i++)
        {
            BodyPart(
                PrimitiveType.Cube,
                $"DetailSpine{i}",
                body.transform,
                new Vector3(0f, 2.35f - i * 0.42f, -0.55f),
                new Vector3(0.2f, 0.7f - i * 0.1f, 0.2f),
                iceMaterial,
                Quaternion.Euler(-30f - i * 6f, 45f, 0f)
            );
        }

        foreach (float side in new[] { -1f, 1f })
        {
            string suffix = side < 0f ? "Left" : "Right";
            BodyPart(
                PrimitiveType.Sphere,
                "EyeGlow" + suffix,
                body.transform,
                new Vector3(side * 0.25f, 2.98f, 0.42f),
                new Vector3(0.19f, 0.19f, 0.1f),
                iceMaterial
            );
            // Pulled in from the torso's side so the shoulder does not float clear of it.
            BodyPart(
                PrimitiveType.Cube,
                "Arm" + suffix,
                body.transform,
                new Vector3(side * 0.95f, 1.6f, 0.05f),
                new Vector3(0.45f, 1.9f, 0.46f),
                bodyMaterial
            );
            BodyPart(
                PrimitiveType.Cube,
                "Leg" + suffix,
                body.transform,
                new Vector3(side * 0.42f, 0.45f, 0f),
                new Vector3(0.5f, 0.9f, 0.52f),
                bodyMaterial
            );
        }

        return body;
    }

    /// <summary>The lava dungeon boss: low, wide and hunched, cracked open and glowing.</summary>
    static GameObject BuildLavaBruteBody(
        Transform parent,
        Material bodyMaterial,
        Material eyeMaterial,
        Material emberMaterial,
        Material clawMaterial
    )
    {
        var body = new GameObject("LavaBruteBody");
        body.transform.SetParent(parent, false);

        BodyPart(
            PrimitiveType.Cube,
            "Torso",
            body.transform,
            new Vector3(0f, 1.35f, 0f),
            new Vector3(1.9f, 1.2f, 1.15f),
            bodyMaterial
        );
        // Bridges the torso down to where the legs meet it, so the two do not read as a box
        // stacked on two boxes.
        BodyPart(
            PrimitiveType.Cube,
            "Hips",
            body.transform,
            new Vector3(0f, 0.75f, 0f),
            new Vector3(1.4f, 0.4f, 1.0f),
            bodyMaterial
        );
        // Shoulders below the head and set back, with the neck carrying the head out in
        // front of them: the beast is hunched, and a hunch is a line from back to jaw, not a
        // second ball parked behind the skull.
        BodyPart(
            PrimitiveType.Cube,
            "Shoulders",
            body.transform,
            new Vector3(0f, 1.86f, -0.22f),
            new Vector3(2.3f, 0.58f, 1.15f),
            bodyMaterial
        );
        BodyPart(
            PrimitiveType.Cube,
            "Neck",
            body.transform,
            new Vector3(0f, 1.98f, 0.26f),
            new Vector3(0.62f, 0.46f, 0.52f),
            bodyMaterial
        );
        BodyPart(
            PrimitiveType.Sphere,
            "Head",
            body.transform,
            new Vector3(0f, 2.05f, 0.6f),
            new Vector3(0.9f, 0.72f, 0.85f),
            bodyMaterial
        );
        BodyPart(
            PrimitiveType.Cube,
            "Jaw",
            body.transform,
            new Vector3(0f, 1.8f, 0.72f),
            new Vector3(0.72f, 0.26f, 0.62f),
            bodyMaterial
        );
        BodyPart(
            PrimitiveType.Cube,
            "DetailMaw",
            body.transform,
            new Vector3(0f, 1.95f, 0.86f),
            new Vector3(0.6f, 0.16f, 0.2f),
            emberMaterial
        );
        BodyPart(
            PrimitiveType.Cube,
            "DetailCrackChest",
            body.transform,
            new Vector3(0f, 1.3f, 0.6f),
            new Vector3(0.22f, 0.85f, 0.06f),
            emberMaterial
        );

        // Spines down the back, tallest at the shoulders and shrinking towards the tail.
        for (int i = 0; i < 5; i++)
        {
            float t = i / 4f;
            BodyPart(
                PrimitiveType.Cube,
                $"DetailSpine{i}",
                body.transform,
                new Vector3(0f, 2.25f - t * 0.55f, -0.35f - t * 0.6f),
                new Vector3(0.22f, 0.7f - t * 0.3f, 0.22f),
                emberMaterial,
                Quaternion.Euler(-24f - i * 8f, 45f, 0f)
            );
        }

        foreach (float side in new[] { -1f, 1f })
        {
            string suffix = side < 0f ? "Left" : "Right";
            BodyPart(
                PrimitiveType.Sphere,
                "EyeGlow" + suffix,
                body.transform,
                new Vector3(side * 0.26f, 2.16f, 0.94f),
                new Vector3(0.17f, 0.17f, 0.1f),
                emberMaterial
            );
            BodyPart(
                PrimitiveType.Cube,
                "Tusk" + suffix,
                body.transform,
                new Vector3(side * 0.26f, 1.88f, 0.92f),
                new Vector3(0.1f, 0.24f, 0.1f),
                clawMaterial
            );
            BodyPart(
                PrimitiveType.Cube,
                "Arm" + suffix,
                body.transform,
                new Vector3(side * 1.22f, 1.15f, 0.1f),
                new Vector3(0.52f, 1.7f, 0.56f),
                bodyMaterial
            );
            BodyPart(
                PrimitiveType.Cube,
                "DetailClawCrack" + suffix,
                body.transform,
                new Vector3(side * 1.22f, 1.35f, 0.4f),
                new Vector3(0.4f, 0.14f, 0.06f),
                emberMaterial
            );
            // Pulled up so the leg's top actually reaches the torso's underside instead of
            // stopping short of it.
            BodyPart(
                PrimitiveType.Cube,
                "Leg" + suffix,
                body.transform,
                new Vector3(side * 0.52f, 0.39f, 0f),
                new Vector3(0.56f, 0.78f, 0.62f),
                bodyMaterial
            );
        }

        return body;
    }

    static GameObject BuildPlayerPrefab()
    {
        var root = new GameObject("PlayerAvatar");

        Material shirtMaterial = CreateMaterial("PlayerShirt", Color.white);
        Material skinMaterial = CreateMaterial("PlayerSkin", new Color(0.95f, 0.79f, 0.66f));
        Material pantsMaterial = CreateMaterial("PlayerPants", new Color(0.22f, 0.26f, 0.36f));
        Material faceMaterial = CreateMaterial("PlayerFace", new Color(0.12f, 0.14f, 0.18f));

        var humanoid = BuildHumanoid(
            root.transform,
            shirtMaterial,
            skinMaterial,
            pantsMaterial,
            faceMaterial
        );

        // Every piece of gear lives in the prefab and is switched on by PlayerGear, the same
        // way a monster picks one of its bodies.
        var weaponModels = new[]
        {
            BuildSword(
                humanoid.RightArm,
                "Sword",
                new Color(0.78f, 0.80f, 0.86f),
                new Color(0.80f, 0.66f, 0.28f)
            ),
            BuildSword(
                humanoid.RightArm,
                "SwordSteel",
                new Color(0.86f, 0.88f, 0.92f),
                new Color(0.62f, 0.64f, 0.70f)
            ),
            BuildSword(
                humanoid.RightArm,
                "SwordFrost",
                new Color(0.70f, 0.88f, 1f),
                new Color(0.42f, 0.66f, 0.88f)
            ),
            BuildSword(
                humanoid.RightArm,
                "SwordEmber",
                new Color(0.42f, 0.30f, 0.28f),
                new Color(1f, 0.48f, 0.14f)
            ),
            BuildRod(humanoid.RightArm),
            // Past the rod: gear the shop sells outright, never dropped by a monster.
            BuildSword(
                humanoid.RightArm,
                "SwordLeather",
                new Color(0.64f, 0.48f, 0.30f),
                new Color(0.40f, 0.28f, 0.16f)
            ),
            BuildSword(
                humanoid.RightArm,
                "SwordSilver",
                new Color(0.90f, 0.92f, 0.94f),
                new Color(0.64f, 0.68f, 0.74f)
            ),
            BuildSword(
                humanoid.RightArm,
                "SwordMithril",
                new Color(0.58f, 0.88f, 0.90f),
                new Color(0.22f, 0.56f, 0.62f)
            ),
        };

        var armorSets = new[]
        {
            BuildArmorSet(
                humanoid,
                "ArmorSteel",
                new Color(0.72f, 0.74f, 0.78f),
                new Color(0.52f, 0.54f, 0.58f)
            ),
            BuildArmorSet(
                humanoid,
                "ArmorFrost",
                new Color(0.72f, 0.86f, 0.96f),
                new Color(0.44f, 0.68f, 0.88f)
            ),
            BuildArmorSet(
                humanoid,
                "ArmorEmber",
                new Color(0.36f, 0.26f, 0.24f),
                new Color(1f, 0.48f, 0.14f)
            ),
        };

        // The catch rides in the armour list past the three sets: never worn, so the only
        // thing that ever asks for these is the inventory icon.
        var fish = new[]
        {
            BuildFish(
                humanoid.Rig,
                "FishCrucian",
                new Color(0.62f, 0.66f, 0.52f),
                new Color(0.44f, 0.48f, 0.38f)
            ),
            BuildFish(
                humanoid.Rig,
                "FishCarp",
                new Color(0.72f, 0.56f, 0.34f),
                new Color(0.52f, 0.38f, 0.22f)
            ),
            BuildFish(
                humanoid.Rig,
                "FishCatfish",
                new Color(0.36f, 0.36f, 0.34f),
                new Color(0.26f, 0.26f, 0.24f)
            ),
            BuildFish(
                humanoid.Rig,
                "FishTrout",
                new Color(0.58f, 0.70f, 0.78f),
                new Color(0.86f, 0.42f, 0.44f)
            ),
            BuildFish(
                humanoid.Rig,
                "FishGolden",
                new Color(0.94f, 0.78f, 0.26f),
                new Color(1f, 0.58f, 0.14f)
            ),
        };

        // The shop's own sets ride past the fish, so PlayerGear.Piece.Theme keeps meaning
        // "index into this list" for every armour piece without reshuffling what the ground
        // already drops.
        var shopArmorSets = new[]
        {
            BuildArmorSet(
                humanoid,
                "ArmorLeather",
                new Color(0.58f, 0.42f, 0.26f),
                new Color(0.38f, 0.26f, 0.16f)
            ),
            BuildArmorSet(
                humanoid,
                "ArmorSilver",
                new Color(0.88f, 0.90f, 0.92f),
                new Color(0.64f, 0.68f, 0.72f)
            ),
            BuildArmorSet(
                humanoid,
                "ArmorMithril",
                new Color(0.56f, 0.86f, 0.88f),
                new Color(0.22f, 0.54f, 0.60f)
            ),
        };

        var armorModels = new List<GameObject>(System.Array.ConvertAll(armorSets, set => set.Body));
        armorModels.AddRange(fish);
        armorModels.AddRange(System.Array.ConvertAll(shopArmorSets, set => set.Body));

        // Left/right arrays line up with the same Theme index as armorModels: the fish stretch
        // in the middle stays null in both, which At() already treats as "no arm piece".
        var armorLeftArms = new GameObject[armorModels.Count];
        var armorRightArms = new GameObject[armorModels.Count];
        for (int i = 0; i < armorSets.Length; i++)
        {
            armorLeftArms[i] = armorSets[i].LeftArm;
            armorRightArms[i] = armorSets[i].RightArm;
        }

        for (int i = 0; i < shopArmorSets.Length; i++)
        {
            int index = armorSets.Length + fish.Length + i;
            armorLeftArms[index] = shopArmorSets[i].LeftArm;
            armorRightArms[index] = shopArmorSets[i].RightArm;
        }

        for (int i = 1; i < weaponModels.Length; i++)
        {
            weaponModels[i].SetActive(false);
        }

        foreach (var set in armorSets)
        {
            set.Body.SetActive(false);
            set.LeftArm.SetActive(false);
            set.RightArm.SetActive(false);
        }

        foreach (var set in shopArmorSets)
        {
            set.Body.SetActive(false);
            set.LeftArm.SetActive(false);
            set.RightArm.SetActive(false);
        }

        foreach (var caught in fish)
        {
            caught.SetActive(false);
        }

        var controller = Walker(root, 1.95f, 0.3f);
        controller.slopeLimit = 50f;
        controller.stepOffset = 0.4f;

        // Four heads of hair in the prefab, of which the avatar shows one.
        Material hairMaterial = CreateMaterial("PlayerHair", Color.white);
        var hairStyles = new GameObject[8];
        for (int i = 0; i < hairStyles.Length; i++)
        {
            hairStyles[i] = BuildHairStyle(humanoid.Rig, i, hairMaterial);
            // One at a time, or the prefab wears all four at once until a spawn sorts it out.
            hairStyles[i].SetActive(i == 1);
        }

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

        avatar.TrouserParts = new[]
        {
            humanoid.LeftLeg.GetComponentInChildren<Renderer>(),
            humanoid.RightLeg.GetComponentInChildren<Renderer>(),
        };

        avatar.SkinnedParts = new[] { humanoid.Head.GetComponent<Renderer>() };

        avatar.HairStyles = hairStyles;
        avatar.NameTagHeight = humanoid.Head.transform.localPosition.y + 0.55f;

        var gear = root.AddComponent<PlayerGear>();
        gear.WeaponModels = weaponModels;
        gear.ArmorModels = armorModels.ToArray();
        gear.ArmorLeftArmModels = armorLeftArms;
        gear.ArmorRightArmModels = armorRightArms;

        root.AddComponent<PlayerStats>();
        root.AddComponent<PlayerCombat>();
        root.AddComponent<PlayerBuffs>();
        root.AddComponent<PlayerInventory>();
        root.AddComponent<PlayerQuests>();
        root.AddComponent<PlayerFishing>();
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
    static Humanoid BuildHumanoid(
        Transform parent,
        Material shirtMaterial,
        Material skinMaterial,
        Material pantsMaterial,
        Material faceMaterial
    )
    {
        var rig = new GameObject("Rig");
        rig.transform.SetParent(parent, false);

        var torso = BodyPart(
            PrimitiveType.Cube,
            "Torso",
            rig.transform,
            new Vector3(0f, 1.15f, 0f),
            new Vector3(0.62f, 0.72f, 0.34f),
            shirtMaterial
        );
        var head = BodyPart(
            PrimitiveType.Cube,
            "Head",
            rig.transform,
            new Vector3(0f, 1.73f, 0f),
            new Vector3(0.44f, 0.44f, 0.42f),
            skinMaterial
        );
        // Two eyes rather than one visor strip, so the face reads as a face.
        BodyPart(
            PrimitiveType.Cube,
            "EyeLeft",
            rig.transform,
            new Vector3(-0.11f, 1.71f, 0.22f),
            new Vector3(0.09f, 0.1f, 0.03f),
            faceMaterial
        );
        BodyPart(
            PrimitiveType.Cube,
            "EyeRight",
            rig.transform,
            new Vector3(0.11f, 1.71f, 0.22f),
            new Vector3(0.09f, 0.1f, 0.03f),
            faceMaterial
        );
        // A line under them, in the same colour. Not on the list of parts the mirror recolours:
        // a mouth is a mouth whatever else the player decides to be.
        BodyPart(
            PrimitiveType.Cube,
            "Mouth",
            rig.transform,
            new Vector3(0f, 1.58f, 0.22f),
            new Vector3(0.11f, 0.04f, 0.03f),
            faceMaterial
        );

        return new Humanoid
        {
            Rig = rig.transform,
            Torso = torso,
            Head = head,
            LeftArm = Limb(
                "LeftArm",
                rig.transform,
                new Vector3(-0.4f, 1.45f, 0f),
                new Vector3(0.18f, 0.62f, 0.24f),
                shirtMaterial
            ),
            RightArm = Limb(
                "RightArm",
                rig.transform,
                new Vector3(0.4f, 1.45f, 0f),
                new Vector3(0.18f, 0.62f, 0.24f),
                shirtMaterial
            ),
            LeftLeg = Limb(
                "LeftLeg",
                rig.transform,
                new Vector3(-0.17f, 0.8f, 0f),
                new Vector3(0.24f, 0.8f, 0.26f),
                pantsMaterial
            ),
            RightLeg = Limb(
                "RightLeg",
                rig.transform,
                new Vector3(0.17f, 0.8f, 0f),
                new Vector3(0.24f, 0.8f, 0.26f),
                pantsMaterial
            ),
        };
    }

    /// <summary>
    /// Sword held in the right hand. It hangs under the arm pivot, so the limb animator's
    /// attack swing carries it along without any extra bone.
    /// </summary>
    static GameObject BuildSword(Transform armPivot, string name, Color blade, Color trim)
    {
        Material gripMaterial = CreateMaterial("SwordGrip", new Color(0.32f, 0.22f, 0.16f));
        Material bladeMaterial = CreateMaterial(name + "Blade", blade);

        var sword = new GameObject(name);
        sword.transform.SetParent(armPivot, false);
        sword.transform.localPosition = new Vector3(0f, -0.62f, 0.06f);
        // Tilted forward so the blade points ahead instead of dragging through the ground.
        sword.transform.localRotation = Quaternion.Euler(-40f, 0f, 0f);

        Material trimMaterial = CreateMaterial(name + "Trim", trim);
        Material fullerMaterial = CreateMaterial(name + "Fuller", blade * 0.7f);

        // Hilt: pommel, wrapped grip, crossguard with turned tips.
        BodyPart(
            PrimitiveType.Cube,
            "Pommel",
            sword.transform,
            new Vector3(0f, 0.07f, 0f),
            new Vector3(0.11f, 0.09f, 0.11f),
            trimMaterial
        );
        BodyPart(
            PrimitiveType.Cube,
            "Grip",
            sword.transform,
            new Vector3(0f, -0.05f, 0f),
            new Vector3(0.07f, 0.2f, 0.07f),
            gripMaterial
        );
        BodyPart(
            PrimitiveType.Cube,
            "WrapUpper",
            sword.transform,
            new Vector3(0f, -0.01f, 0f),
            new Vector3(0.085f, 0.02f, 0.085f),
            trimMaterial
        );
        BodyPart(
            PrimitiveType.Cube,
            "WrapLower",
            sword.transform,
            new Vector3(0f, -0.09f, 0f),
            new Vector3(0.085f, 0.02f, 0.085f),
            trimMaterial
        );

        BodyPart(
            PrimitiveType.Cube,
            "Guard",
            sword.transform,
            new Vector3(0f, -0.17f, 0f),
            new Vector3(0.3f, 0.06f, 0.1f),
            trimMaterial
        );
        foreach (float side in new[] { -1f, 1f })
        {
            BodyPart(
                PrimitiveType.Cube,
                side < 0f ? "GuardTipLeft" : "GuardTipRight",
                sword.transform,
                new Vector3(side * 0.17f, -0.145f, 0f),
                new Vector3(0.07f, 0.09f, 0.09f),
                trimMaterial,
                Quaternion.Euler(0f, 0f, side * 28f)
            );
        }

        // Blade: a wide upper half, a narrower lower half, a fuller down the middle, a point.
        BodyPart(
            PrimitiveType.Cube,
            "Ricasso",
            sword.transform,
            new Vector3(0f, -0.23f, 0f),
            new Vector3(0.1f, 0.07f, 0.05f),
            bladeMaterial
        );
        BodyPart(
            PrimitiveType.Cube,
            "BladeUpper",
            sword.transform,
            new Vector3(0f, -0.42f, 0f),
            new Vector3(0.115f, 0.34f, 0.042f),
            bladeMaterial
        );
        BodyPart(
            PrimitiveType.Cube,
            "BladeLower",
            sword.transform,
            new Vector3(0f, -0.68f, 0f),
            new Vector3(0.085f, 0.22f, 0.036f),
            bladeMaterial
        );
        BodyPart(
            PrimitiveType.Cube,
            "Fuller",
            sword.transform,
            new Vector3(0f, -0.5f, 0.024f),
            new Vector3(0.032f, 0.5f, 0.012f),
            fullerMaterial
        );

        BodyPart(
            PrimitiveType.Cube,
            "Point",
            sword.transform,
            new Vector3(0f, -0.81f, 0f),
            new Vector3(0.062f, 0.062f, 0.036f),
            bladeMaterial,
            Quaternion.Euler(0f, 0f, 45f)
        );
        return sword;
    }

    /// <summary>A fish: body, tail, fins and an eye. Only ever seen in the inventory.</summary>
    static GameObject BuildFish(Transform rig, string name, Color body, Color fin)
    {
        Material bodyMaterial = CreateMaterial(name + "Body", body);
        Material finMaterial = CreateMaterial(name + "Fin", fin);
        Material eyeMaterial = CreateMaterial("FishEye", new Color(0.08f, 0.08f, 0.10f));

        var caught = new GameObject(name);
        caught.transform.SetParent(rig, false);
        caught.transform.localPosition = new Vector3(0f, 1.2f, 0.5f);

        Material bellyMaterial = CreateMaterial(
            name + "Belly",
            Color.Lerp(body, Color.white, 0.45f)
        );

        // Two lumps so it has shoulders and tapers to the tail, pale underneath.
        BodyPart(
            PrimitiveType.Sphere,
            "Body",
            caught.transform,
            Vector3.zero,
            new Vector3(0.44f, 0.26f, 0.2f),
            bodyMaterial
        );
        BodyPart(
            PrimitiveType.Sphere,
            "Waist",
            caught.transform,
            new Vector3(-0.2f, 0f, 0f),
            new Vector3(0.26f, 0.16f, 0.12f),
            bodyMaterial
        );
        BodyPart(
            PrimitiveType.Sphere,
            "Belly",
            caught.transform,
            new Vector3(-0.02f, -0.07f, 0f),
            new Vector3(0.36f, 0.14f, 0.17f),
            bellyMaterial
        );
        BodyPart(
            PrimitiveType.Sphere,
            "Head",
            caught.transform,
            new Vector3(0.24f, 0.01f, 0f),
            new Vector3(0.22f, 0.21f, 0.19f),
            bodyMaterial
        );
        BodyPart(
            PrimitiveType.Cube,
            "Mouth",
            caught.transform,
            new Vector3(0.35f, -0.02f, 0f),
            new Vector3(0.06f, 0.04f, 0.1f),
            finMaterial
        );
        BodyPart(
            PrimitiveType.Cube,
            "Gill",
            caught.transform,
            new Vector3(0.15f, 0f, 0f),
            new Vector3(0.02f, 0.17f, 0.17f),
            finMaterial
        );

        // Forked tail, and the fins that say which way is up.
        BodyPart(
            PrimitiveType.Cube,
            "TailStem",
            caught.transform,
            new Vector3(-0.3f, 0f, 0f),
            new Vector3(0.08f, 0.08f, 0.04f),
            bodyMaterial
        );
        BodyPart(
            PrimitiveType.Cube,
            "TailUpper",
            caught.transform,
            new Vector3(-0.38f, 0.08f, 0f),
            new Vector3(0.17f, 0.15f, 0.025f),
            finMaterial,
            Quaternion.Euler(0f, 0f, 32f)
        );
        BodyPart(
            PrimitiveType.Cube,
            "TailLower",
            caught.transform,
            new Vector3(-0.38f, -0.08f, 0f),
            new Vector3(0.17f, 0.15f, 0.025f),
            finMaterial,
            Quaternion.Euler(0f, 0f, -32f)
        );
        BodyPart(
            PrimitiveType.Cube,
            "FinTop",
            caught.transform,
            new Vector3(-0.02f, 0.16f, 0f),
            new Vector3(0.2f, 0.13f, 0.02f),
            finMaterial,
            Quaternion.Euler(0f, 0f, 16f)
        );
        BodyPart(
            PrimitiveType.Cube,
            "FinBottom",
            caught.transform,
            new Vector3(-0.1f, -0.13f, 0f),
            new Vector3(0.12f, 0.09f, 0.02f),
            finMaterial,
            Quaternion.Euler(0f, 0f, -20f)
        );

        foreach (float side in new[] { -1f, 1f })
        {
            BodyPart(
                PrimitiveType.Cube,
                side < 0f ? "FinLeft" : "FinRight",
                caught.transform,
                new Vector3(0.08f, -0.05f, side * 0.09f),
                new Vector3(0.13f, 0.05f, 0.1f),
                finMaterial,
                Quaternion.Euler(side * 22f, 0f, -14f)
            );
            BodyPart(
                PrimitiveType.Sphere,
                side < 0f ? "EyeLeft" : "EyeRight",
                caught.transform,
                new Vector3(0.3f, 0.05f, side * 0.075f),
                new Vector3(0.06f, 0.06f, 0.05f),
                eyeMaterial
            );
        }

        return caught;
    }

    /// <summary>A fishing rod: cork grip, a tapering pole and a line hanging off the tip.</summary>
    static GameObject BuildRod(Transform armPivot)
    {
        Material gripMaterial = CreateMaterial("RodGrip", new Color(0.42f, 0.28f, 0.18f));
        Material poleMaterial = CreateMaterial("RodPole", new Color(0.30f, 0.26f, 0.24f));
        Material lineMaterial = CreateMaterial("RodLine", new Color(0.86f, 0.88f, 0.90f));

        var rod = new GameObject("Rod");
        rod.transform.SetParent(armPivot, false);
        rod.transform.localPosition = new Vector3(0f, -0.62f, 0.06f);
        rod.transform.localRotation = Quaternion.Euler(-62f, 0f, 0f);

        Material trimMaterial = CreateMaterial("RodTrim", new Color(0.72f, 0.60f, 0.30f));

        // Butt cap, cork grip with two bindings, then the reel seat.
        BodyPart(
            PrimitiveType.Cube,
            "Butt",
            rod.transform,
            new Vector3(0f, 0.11f, 0f),
            new Vector3(0.085f, 0.05f, 0.085f),
            trimMaterial
        );
        BodyPart(
            PrimitiveType.Cube,
            "Grip",
            rod.transform,
            new Vector3(0f, -0.02f, 0f),
            new Vector3(0.07f, 0.24f, 0.07f),
            gripMaterial
        );
        BodyPart(
            PrimitiveType.Cube,
            "BindUpper",
            rod.transform,
            new Vector3(0f, 0.05f, 0f),
            new Vector3(0.08f, 0.02f, 0.08f),
            trimMaterial
        );
        BodyPart(
            PrimitiveType.Cube,
            "BindLower",
            rod.transform,
            new Vector3(0f, -0.11f, 0f),
            new Vector3(0.08f, 0.02f, 0.08f),
            trimMaterial
        );

        // The reel: a drum on a foot with a handle out of the side.
        BodyPart(
            PrimitiveType.Cube,
            "ReelFoot",
            rod.transform,
            new Vector3(0f, -0.15f, 0.04f),
            new Vector3(0.05f, 0.06f, 0.05f),
            poleMaterial
        );
        BodyPart(
            PrimitiveType.Cylinder,
            "ReelDrum",
            rod.transform,
            new Vector3(0f, -0.19f, 0.09f),
            new Vector3(0.12f, 0.03f, 0.12f),
            trimMaterial,
            Quaternion.Euler(0f, 0f, 90f)
        );
        BodyPart(
            PrimitiveType.Cube,
            "ReelHandle",
            rod.transform,
            new Vector3(0.09f, -0.19f, 0.09f),
            new Vector3(0.09f, 0.02f, 0.02f),
            poleMaterial
        );
        BodyPart(
            PrimitiveType.Cube,
            "ReelKnob",
            rod.transform,
            new Vector3(0.14f, -0.19f, 0.09f),
            new Vector3(0.03f, 0.04f, 0.03f),
            gripMaterial
        );

        // Three tapering sections with a guide ring at each joint.
        BodyPart(
            PrimitiveType.Cube,
            "PoleLower",
            rod.transform,
            new Vector3(0f, -0.42f, 0f),
            new Vector3(0.05f, 0.42f, 0.05f),
            poleMaterial
        );
        BodyPart(
            PrimitiveType.Cube,
            "PoleMiddle",
            rod.transform,
            new Vector3(0f, -0.78f, 0f),
            new Vector3(0.036f, 0.34f, 0.036f),
            poleMaterial
        );
        BodyPart(
            PrimitiveType.Cube,
            "PoleTip",
            rod.transform,
            new Vector3(0f, -1.06f, 0f),
            new Vector3(0.022f, 0.26f, 0.022f),
            poleMaterial
        );

        for (int i = 0; i < 3; i++)
        {
            float y = -0.5f - i * 0.28f;
            BodyPart(
                PrimitiveType.Cube,
                $"Guide{i}",
                rod.transform,
                new Vector3(0f, y, 0.05f),
                new Vector3(0.05f, 0.012f, 0.05f),
                trimMaterial
            );
            BodyPart(
                PrimitiveType.Cube,
                $"GuideStem{i}",
                rod.transform,
                new Vector3(0f, y, 0.028f),
                new Vector3(0.012f, 0.012f, 0.03f),
                trimMaterial
            );
        }

        // Line down the guides and a hook swinging off the tip.
        BodyPart(
            PrimitiveType.Cube,
            "Line",
            rod.transform,
            new Vector3(0f, -0.72f, 0.05f),
            new Vector3(0.008f, 0.56f, 0.008f),
            lineMaterial
        );
        BodyPart(
            PrimitiveType.Cube,
            "LineDrop",
            rod.transform,
            new Vector3(0f, -1.26f, 0.03f),
            new Vector3(0.008f, 0.16f, 0.008f),
            lineMaterial
        );
        BodyPart(
            PrimitiveType.Cube,
            "Hook",
            rod.transform,
            new Vector3(0f, -1.35f, 0.05f),
            new Vector3(0.012f, 0.05f, 0.05f),
            trimMaterial,
            Quaternion.Euler(35f, 0f, 0f)
        );
        return rod;
    }

    /// <summary>
    /// A set of armour: breastplate, collar and belt over the torso, and a shoulder piece
    /// hanging off each arm pivot so it swings when the arm does.
    /// </summary>
    static (GameObject Body, GameObject LeftArm, GameObject RightArm) BuildArmorSet(
        Humanoid humanoid,
        string name,
        Color plate,
        Color trim
    )
    {
        Material plateMaterial = CreateMaterial(name + "Plate", plate);
        Material trimMaterial = CreateMaterial(name + "Trim", trim);

        var armor = new GameObject(name);
        armor.transform.SetParent(humanoid.Rig, false);

        BodyPart(
            PrimitiveType.Cube,
            "Chest",
            armor.transform,
            new Vector3(0f, 1.18f, 0f),
            new Vector3(0.68f, 0.62f, 0.4f),
            plateMaterial
        );
        BodyPart(
            PrimitiveType.Cube,
            "Ridge",
            armor.transform,
            new Vector3(0f, 1.18f, 0.2f),
            new Vector3(0.1f, 0.6f, 0.04f),
            trimMaterial
        );
        BodyPart(
            PrimitiveType.Cube,
            "Collar",
            armor.transform,
            new Vector3(0f, 1.48f, 0f),
            new Vector3(0.5f, 0.1f, 0.42f),
            trimMaterial
        );
        BodyPart(
            PrimitiveType.Cube,
            "Belt",
            armor.transform,
            new Vector3(0f, 0.86f, 0f),
            new Vector3(0.7f, 0.1f, 0.42f),
            trimMaterial
        );

        var sleeves = new GameObject[2];
        for (int i = 0; i < 2; i++)
        {
            float side = i == 0 ? -1f : 1f;
            Transform arm = i == 0 ? humanoid.LeftArm : humanoid.RightArm;

            // Under the arm pivot, which is the thing the attack swings.
            var sleeve = new GameObject(name + (i == 0 ? "LeftArm" : "RightArm"));
            sleeve.transform.SetParent(arm, false);

            BodyPart(
                PrimitiveType.Cube,
                "Pauldron",
                sleeve.transform,
                new Vector3(-side * 0.02f, -0.03f, 0f),
                new Vector3(0.26f, 0.22f, 0.4f),
                plateMaterial,
                Quaternion.Euler(0f, 0f, side * 12f)
            );

            sleeves[i] = sleeve;
        }

        return (armor, sleeves[0], sleeves[1]);
    }

    /// <summary>
    /// The fence every area is boxed in by: four slabs on the edge of a sixty unit square.
    /// One place, because the four of them only work if they agree on the same square.
    /// </summary>
    static void BuildBounds(Transform parent, string prefix, Material material)
    {
        const float half = AreaHalfWidth;
        CreatePrimitive(
            PrimitiveType.Cube,
            prefix + "WallNorth",
            parent,
            new Vector3(0f, 2f, half),
            new Vector3(60f, 4f, 1f),
            material
        );
        CreatePrimitive(
            PrimitiveType.Cube,
            prefix + "WallSouth",
            parent,
            new Vector3(0f, 2f, -half),
            new Vector3(60f, 4f, 1f),
            material
        );
        CreatePrimitive(
            PrimitiveType.Cube,
            prefix + "WallEast",
            parent,
            new Vector3(half, 2f, 0f),
            new Vector3(1f, 4f, 60f),
            material
        );
        CreatePrimitive(
            PrimitiveType.Cube,
            prefix + "WallWest",
            parent,
            new Vector3(-half, 2f, 0f),
            new Vector3(1f, 4f, 60f),
            material
        );
    }

    /// <summary>
    /// The capsule anything that walks is moved by. A controller parks its capsule a skin
    /// width clear of the floor, so the body hangs that far above it unless the capsule is
    /// lifted by the same amount inside the body: half the height plus the skin is what puts
    /// the feet on the ground, whatever the thing is the size of.
    /// </summary>
    static CharacterController Walker(GameObject root, float height, float radius)
    {
        var controller = root.AddComponent<CharacterController>();
        controller.height = height;
        controller.radius = radius;
        controller.skinWidth = 0.03f;
        controller.center = new Vector3(0f, height * 0.5f + controller.skinWidth, 0f);
        return controller;
    }

    /// <summary>Static body part with its primitive collider removed.</summary>
    static GameObject BodyPart(
        PrimitiveType type,
        string name,
        Transform parent,
        Vector3 localPosition,
        Vector3 localScale,
        Material material
    )
    {
        var part = CreatePrimitive(type, name, parent, localPosition, localScale, material);
        Object.DestroyImmediate(part.GetComponent<Collider>());
        return part;
    }

    /// <summary>The same, turned: every tilted part would otherwise need its own second line.</summary>
    static GameObject BodyPart(
        PrimitiveType type,
        string name,
        Transform parent,
        Vector3 localPosition,
        Vector3 localScale,
        Material material,
        Quaternion rotation
    )
    {
        var part = BodyPart(type, name, parent, localPosition, localScale, material);
        part.transform.localRotation = rotation;
        return part;
    }

    static GameObject CreatePrimitive(
        PrimitiveType type,
        string name,
        Transform parent,
        Vector3 localPosition,
        Vector3 localScale,
        Material material,
        Quaternion rotation
    )
    {
        var part = CreatePrimitive(type, name, parent, localPosition, localScale, material);
        part.transform.localRotation = rotation;
        return part;
    }

    /// <summary>Empty pivot at the shoulder/hip with the limb hanging below it, so rotating the pivot swings the limb.</summary>
    static Transform Limb(
        string name,
        Transform parent,
        Vector3 pivotPosition,
        Vector3 size,
        Material material
    )
    {
        var pivot = new GameObject(name);
        pivot.transform.SetParent(parent, false);
        pivot.transform.localPosition = pivotPosition;
        BodyPart(
            PrimitiveType.Cube,
            name + "Mesh",
            pivot.transform,
            new Vector3(0f, -size.y * 0.5f, 0f),
            size,
            material
        );
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
            Debug.LogWarning(
                $"{NetworkPrefabsListPath} not found; register {prefab.name} by hand."
            );
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

        camera.transform.SetPositionAndRotation(
            new Vector3(0f, 8f, -12f),
            Quaternion.Euler(20f, 0f, 0f)
        );
        camera.farClipPlane = 300f;

        if (camera.GetComponent<FollowCamera>() == null)
        {
            camera.gameObject.AddComponent<FollowCamera>();
        }

        // The ear rides with the camera; without it nothing is heard at all.
        if (camera.GetComponent<AudioListener>() == null)
        {
            camera.gameObject.AddComponent<AudioListener>();
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

    static GameObject CreatePrimitive(
        PrimitiveType type,
        string name,
        Transform parent,
        Vector3 localPosition,
        Vector3 localScale,
        Material material
    )
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
    static GameObject CreateDisc(
        string name,
        Transform parent,
        Vector3 localPosition,
        float diameter,
        Material material
    )
    {
        var disc = CreatePrimitive(
            PrimitiveType.Cylinder,
            name,
            parent,
            localPosition,
            new Vector3(diameter, 0.02f, diameter),
            material
        );
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

        // Monsters and players are painted through property blocks, which only batch when the
        // material allows instancing.
        material.enableInstancing = true;
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
