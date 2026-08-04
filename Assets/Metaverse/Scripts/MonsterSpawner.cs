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

    static readonly (string name, int level, int maxHp, Color tint)[] Kinds =
    {
        ("Slime", 1, 40, new Color(0.42f, 0.82f, 0.45f)),
        ("Goblin", 2, 70, new Color(0.85f, 0.72f, 0.30f)),
        ("Orc", 3, 110, new Color(0.80f, 0.35f, 0.30f)),
    };

    bool spawned;

    void Update()
    {
        if (spawned || MonsterPrefab == null)
        {
            return;
        }

        var manager = NetworkManager.Singleton;
        if (manager == null || !manager.IsListening || !manager.IsServer)
        {
            return;
        }

        spawned = true;
        SpawnAll();
    }

    void SpawnAll()
    {
        for (int i = 0; i < Count; i++)
        {
            var kind = Kinds[i % Kinds.Length];

            // Spread the pack evenly around the field instead of clumping at the centre.
            float angle = (i * 360f / Count + kind.level * 11f) * Mathf.Deg2Rad;
            float distance = Mathf.Lerp(6f, Radius, (i % 4) / 3f);
            Vector3 position = transform.position + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * distance;

            var instance = Instantiate(MonsterPrefab, position, Quaternion.identity);
            instance.name = $"{kind.name}{i}";
            instance.GetComponent<NetworkObject>().Spawn();
            instance.GetComponent<Monster>().Configure(kind.name, kind.level, kind.maxHp, kind.tint);
        }

        Debug.Log($"[Metaverse] spawned {Count} monsters at {transform.position}");
    }
}
