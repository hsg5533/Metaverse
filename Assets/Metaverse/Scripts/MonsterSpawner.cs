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
            int kind = i % Monster.Kinds.Length;

            // Weak kinds near the middle, tough ones out at the rim, so walking further out
            // is what raises the difficulty.
            float angle = (i * 360f / Count + kind * 11f) * Mathf.Deg2Rad;
            float ring = Monster.Kinds.Length > 1 ? kind / (float)(Monster.Kinds.Length - 1) : 0f;
            float distance = Mathf.Lerp(8f, Radius, ring) + (i % 3) * 1.5f;
            Vector3 position = transform.position + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * distance;

            var instance = Instantiate(MonsterPrefab, position, Quaternion.identity);
            instance.name = $"{Monster.Kinds[kind].Name}{i}";
            instance.GetComponent<NetworkObject>().Spawn();

            // The monster works out its own level from the players in the world.
            instance.GetComponent<Monster>().Configure(kind);
        }

        Debug.Log($"[Metaverse] spawned {Count} monsters at {transform.position}");
    }
}
