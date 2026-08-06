using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Fills the hunting field with monsters once the server is running. Spawned monsters
/// revive themselves after being killed, so this only ever runs a single pass.
/// </summary>
public class MonsterSpawner : MonoBehaviour
{
    public GameObject MonsterPrefab;
    public int Count = 12;
    public float Radius = 22f;

    /// <summary>Levels added on top of the players' level, for tougher areas.</summary>
    public int LevelBonus;

    /// <summary>Which ground this is: index into <see cref="Monster.Rosters"/>.</summary>
    public int Theme;

    /// <summary>Spawns a single boss standing on this spot instead of a pack.</summary>
    public bool Boss;

    bool spawned;

    void Update()
    {
        var manager = NetworkManager.Singleton;
        if (manager == null || !manager.IsListening || !manager.IsServer)
        {
            // Play mode can start without reloading the scene, so the flag has to clear
            // itself between sessions or the second run comes up empty.
            spawned = false;
            return;
        }

        if (spawned || MonsterPrefab == null)
        {
            return;
        }

        spawned = true;
        SpawnAll();
    }

    void SpawnAll()
    {
        if (Boss)
        {
            var bossInstance = Instantiate(MonsterPrefab, transform.position, Quaternion.identity);
            bossInstance.name = Monster.BossNameFor(Theme);
            bossInstance.GetComponent<NetworkObject>().Spawn();
            bossInstance.GetComponent<Monster>().Configure(0, LevelBonus, true, Theme);
            Debug.Log($"[Metaverse] boss placed at {transform.position}");
            return;
        }

        int[] roster = Monster.RosterFor(Theme);

        for (int i = 0; i < Count; i++)
        {
            int slot = i % roster.Length;
            int kind = roster[slot];

            // Weak kinds near the middle, tough ones out at the rim, so walking further out
            // is what raises the difficulty.
            float angle = (i * 360f / Count + slot * 11f) * Mathf.Deg2Rad;
            float ring = roster.Length > 1 ? slot / (float)(roster.Length - 1) : 0f;
            // The inner ring and the jitter both scale with the area, so a narrow corridor
            // does not end up with monsters standing inside its walls.
            float distance = Mathf.Lerp(Mathf.Min(8f, Radius), Radius, ring) + (i % 3) * Radius * 0.1f;
            Vector3 position = transform.position + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * distance;

            var instance = Instantiate(MonsterPrefab, position, Quaternion.identity);
            instance.name = $"{Monster.Kinds[kind].Name}{i}";
            instance.GetComponent<NetworkObject>().Spawn();

            // The monster works out its own level from the players in the world.
            instance.GetComponent<Monster>().Configure(kind, LevelBonus, false, Theme);
        }

        Debug.Log($"[Metaverse] spawned {Count} monsters at {transform.position}");
    }
}
