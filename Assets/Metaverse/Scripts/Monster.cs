using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Hunting field monster. The server runs the chase/attack logic and owns health;
/// the NetworkTransform on the same prefab replicates the movement.
/// A killed monster stays in place, hidden, and revives after <see cref="RespawnDelay"/>.
/// </summary>
public class Monster : NetworkBehaviour
{
    /// <summary>Every spawned monster, used by the server to resolve player attacks.</summary>
    public static readonly List<Monster> All = new();

    public Renderer[] ColoredParts;

    public float MoveSpeed = 2.4f;
    public float AggroRange = 12f;
    public float AttackRange = 1.9f;
    public float AttackCooldown = 1.6f;
    public float LeashRange = 26f;
    public float RespawnDelay = 6f;
    public float NameTagHeight = 1.8f;

    public NetworkVariable<FixedString64Bytes> MonsterName =
        new(new FixedString64Bytes("Slime"), writePerm: NetworkVariableWritePermission.Server);

    public NetworkVariable<int> Level = new(1, writePerm: NetworkVariableWritePermission.Server);
    public NetworkVariable<int> Hp = new(40, writePerm: NetworkVariableWritePermission.Server);
    public NetworkVariable<int> MaxHp = new(40, writePerm: NetworkVariableWritePermission.Server);
    public NetworkVariable<Color> Tint = new(new Color(0.4f, 0.8f, 0.4f), writePerm: NetworkVariableWritePermission.Server);

    public bool IsAlive => Hp.Value > 0;
    public int Damage => 4 + Level.Value * 3;
    public int ExpReward => 12 + Level.Value * 8;
    public int GoldReward => 4 + Level.Value * 3;

    public float HitFlashDuration = 0.25f;

    Renderer[] renderers;
    Collider[] colliders;
    Vector3 home;
    Vector3 baseScale;
    float nextAttackTime;
    float reviveTime;
    float hitFlashEndTime;
    bool visualsAlive = true;
    bool flashing;
    static GUIStyle nameTagStyle;

    void Awake()
    {
        renderers = GetComponentsInChildren<Renderer>();
        colliders = GetComponentsInChildren<Collider>();
        home = transform.position;
        baseScale = transform.localScale;
    }

    public override void OnNetworkSpawn()
    {
        All.Add(this);
        Tint.OnValueChanged += OnTintChanged;
        // Health is replicated to everyone, so the hit reaction needs no extra RPC.
        Hp.OnValueChanged += OnHpChanged;
        ApplyTint(Tint.Value);
    }

    public override void OnNetworkDespawn()
    {
        All.Remove(this);
        Tint.OnValueChanged -= OnTintChanged;
        Hp.OnValueChanged -= OnHpChanged;
    }

    void OnHpChanged(int previous, int current)
    {
        if (current < previous)
        {
            hitFlashEndTime = Time.time + HitFlashDuration;
        }
    }

    /// <summary>Local only: flash white and squash for a moment after being hit.</summary>
    void UpdateHitFlash()
    {
        float remaining = hitFlashEndTime - Time.time;
        if (remaining <= 0f)
        {
            if (flashing)
            {
                flashing = false;
                transform.localScale = baseScale;
                ApplyTint(Tint.Value);
            }
            return;
        }

        flashing = true;
        float strength = remaining / HitFlashDuration;
        ApplyTint(Color.Lerp(Tint.Value, Color.white, strength));
        transform.localScale = Vector3.Scale(baseScale, new Vector3(1f + 0.2f * strength, 1f - 0.2f * strength, 1f + 0.2f * strength));
    }

    /// <summary>Server side: give the freshly spawned monster its kind, stats and home point.</summary>
    public void Configure(string monsterName, int level, int maxHp, Color tint)
    {
        MonsterName.Value = NetText.Trim64(monsterName);
        Level.Value = level;
        MaxHp.Value = maxHp;
        Hp.Value = maxHp;
        Tint.Value = tint;
        home = transform.position;
    }

    /// <summary>Server side: apply a player hit and hand out the reward on the killing blow.</summary>
    public void TakeDamage(int amount, PlayerStats attacker)
    {
        if (!IsServer || !IsAlive || amount <= 0)
        {
            return;
        }

        Hp.Value = Mathf.Max(0, Hp.Value - amount);
        if (Hp.Value > 0)
        {
            return;
        }

        reviveTime = Time.time + RespawnDelay;
        if (attacker != null)
        {
            attacker.GainReward(ExpReward, GoldReward);
        }
    }

    /// <summary>Server side: closest living monster inside the attacker's cone.</summary>
    public static Monster FindTarget(Vector3 origin, Vector3 forward, float range, float coneDegrees)
    {
        Monster best = null;
        float bestDistance = range;

        foreach (var monster in All)
        {
            if (monster == null || !monster.IsAlive)
            {
                continue;
            }

            Vector3 to = monster.transform.position + Vector3.up * 0.5f - origin;
            float distance = to.magnitude;
            if (distance > bestDistance)
            {
                continue;
            }

            to.y = 0f;
            if (to.sqrMagnitude > 0.001f && Vector3.Angle(forward, to) > coneDegrees * 0.5f)
            {
                continue;
            }

            bestDistance = distance;
            best = monster;
        }

        return best;
    }

    void Update()
    {
        SyncVisuals();
        UpdateHitFlash();

        if (!IsServer || !IsSpawned)
        {
            return;
        }

        if (!IsAlive)
        {
            if (Time.time >= reviveTime)
            {
                Revive();
            }
            return;
        }

        // Too far from its post: walk back and ignore everyone on the way.
        if (Vector3.Distance(transform.position, home) > LeashRange)
        {
            MoveTowards(home);
            return;
        }

        var target = NearestPlayer();
        if (target == null)
        {
            return;
        }

        Vector3 targetPosition = target.transform.position;
        if (Vector3.Distance(FlatPosition(targetPosition), FlatPosition(transform.position)) > AttackRange)
        {
            MoveTowards(targetPosition);
            return;
        }

        FaceTowards(targetPosition);
        if (Time.time >= nextAttackTime)
        {
            nextAttackTime = Time.time + AttackCooldown;
            target.TakeDamage(Damage, $"{MonsterName.Value} Lv.{Level.Value}");
        }
    }

    void Revive()
    {
        Hp.Value = MaxHp.Value;
        transform.position = home;
    }

    PlayerStats NearestPlayer()
    {
        PlayerStats best = null;
        float bestDistance = AggroRange;

        foreach (var client in NetworkManager.ConnectedClientsList)
        {
            var playerObject = client.PlayerObject;
            if (playerObject == null)
            {
                continue;
            }

            var stats = playerObject.GetComponent<PlayerStats>();
            if (stats == null)
            {
                continue;
            }

            float distance = Vector3.Distance(playerObject.transform.position, transform.position);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = stats;
            }
        }

        return best;
    }

    void MoveTowards(Vector3 destination)
    {
        Vector3 step = FlatPosition(destination) - FlatPosition(transform.position);
        if (step.sqrMagnitude < 0.0001f)
        {
            return;
        }

        Vector3 position = transform.position + step.normalized * MoveSpeed * Time.deltaTime;
        position.y = home.y;
        transform.position = position;
        FaceTowards(destination);
    }

    void FaceTowards(Vector3 target)
    {
        Vector3 direction = FlatPosition(target) - FlatPosition(transform.position);
        if (direction.sqrMagnitude > 0.0001f)
        {
            transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(direction), 8f * Time.deltaTime);
        }
    }

    static Vector3 FlatPosition(Vector3 position)
    {
        position.y = 0f;
        return position;
    }

    void SyncVisuals()
    {
        bool alive = IsAlive;
        if (alive == visualsAlive)
        {
            return;
        }

        visualsAlive = alive;
        foreach (var renderer in renderers)
        {
            renderer.enabled = alive;
        }
        foreach (var collider in colliders)
        {
            collider.enabled = alive;
        }
    }

    void OnTintChanged(Color previous, Color current)
    {
        ApplyTint(current);
    }

    void ApplyTint(Color color)
    {
        if (ColoredParts == null)
        {
            return;
        }

        foreach (var part in ColoredParts)
        {
            if (part != null)
            {
                part.material.color = color;
            }
        }
    }

    void OnGUI()
    {
        if (!IsSpawned || !IsAlive)
        {
            return;
        }

        var camera = Camera.main;
        if (camera == null || Vector3.Distance(camera.transform.position, transform.position) > 40f)
        {
            return;
        }

        Vector3 screenPoint = camera.WorldToScreenPoint(transform.position + Vector3.up * NameTagHeight);
        if (screenPoint.z <= 0f)
        {
            return;
        }

        nameTagStyle ??= new GUIStyle(GUI.skin.label)
        {
            alignment = TextAnchor.MiddleCenter,
        };

        float x = screenPoint.x;
        float y = Screen.height - screenPoint.y;

        var label = new GUIContent($"{MonsterName.Value} Lv.{Level.Value}");
        Vector2 size = nameTagStyle.CalcSize(label);
        GUI.Label(new Rect(x - size.x * 0.5f, y - size.y, size.x, size.y), label, nameTagStyle);

        const float barWidth = 60f;
        float fill = MaxHp.Value > 0 ? Mathf.Clamp01(Hp.Value / (float)MaxHp.Value) : 0f;
        Color previous = GUI.color;
        GUI.color = new Color(0f, 0f, 0f, 0.6f);
        GUI.DrawTexture(new Rect(x - barWidth * 0.5f, y, barWidth, 5f), Texture2D.whiteTexture);
        GUI.color = new Color(0.85f, 0.25f, 0.25f, 0.95f);
        GUI.DrawTexture(new Rect(x - barWidth * 0.5f, y, barWidth * fill, 5f), Texture2D.whiteTexture);
        GUI.color = previous;
    }
}
