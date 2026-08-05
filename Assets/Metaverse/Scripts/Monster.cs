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

    /// <summary>
    /// The three kinds of monster in the field. Their level is not fixed: it follows the
    /// players, so a Slime is always "your level" and an Orc is always two above it.
    /// </summary>
    public readonly struct Kind
    {
        public readonly string Name;
        public readonly int LevelOffset;
        public readonly Color Tint;

        public Kind(string name, int levelOffset, Color tint)
        {
            Name = name;
            LevelOffset = levelOffset;
            Tint = tint;
        }
    }

    public static readonly Kind[] Kinds =
    {
        new("Slime", 0, new Color(0.42f, 0.82f, 0.45f)),
        new("Goblin", 1, new Color(0.85f, 0.72f, 0.30f)),
        new("Orc", 2, new Color(0.80f, 0.35f, 0.30f)),
    };

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

    // Damage grows faster than the players' defence (+2 a level), so higher level monsters
    // actually hurt more instead of being shrugged off.
    public int Damage => 6 + Level.Value * 4;

    /// <summary>Higher level monsters are visibly bigger, so a tough one reads at a glance.</summary>
    public float LevelScale => Mathf.Min(1f + 0.04f * (Level.Value - 1), 1.8f);
    public int ExpReward => 12 + Level.Value * 8;
    public int GoldReward => 4 + Level.Value * 3;

    public float HitFlashDuration = 0.25f;

    Renderer[] renderers;
    Collider[] colliders;
    int kindIndex;
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
        Level.OnValueChanged += OnLevelChanged;
        // Health is replicated to everyone, so the hit reaction needs no extra RPC.
        Hp.OnValueChanged += OnHpChanged;
        ApplyTint(BodyColor);
    }

    public override void OnNetworkDespawn()
    {
        All.Remove(this);
        Tint.OnValueChanged -= OnTintChanged;
        Level.OnValueChanged -= OnLevelChanged;
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
        Vector3 bodyScale = baseScale * LevelScale;
        float remaining = hitFlashEndTime - Time.time;

        if (remaining <= 0f)
        {
            transform.localScale = bodyScale;
            if (flashing)
            {
                flashing = false;
                ApplyTint(BodyColor);
            }
            return;
        }

        flashing = true;
        float strength = remaining / HitFlashDuration;
        ApplyTint(Color.Lerp(BodyColor, Color.white, strength));
        transform.localScale = Vector3.Scale(bodyScale, new Vector3(1f + 0.2f * strength, 1f - 0.2f * strength, 1f + 0.2f * strength));
    }

    /// <summary>The kind's colour, deepened as the monster levels so tough ones look tough.</summary>
    Color BodyColor => Color.Lerp(Tint.Value, Tint.Value * 0.45f, Mathf.Min((Level.Value - 1) * 0.06f, 1f));

    /// <summary>Server side: give the freshly spawned monster its kind, stats and home point.</summary>
    public void Configure(int kind)
    {
        kindIndex = Mathf.Clamp(kind, 0, Kinds.Length - 1);
        ApplyLevel();
        home = transform.position;
    }

    /// <summary>
    /// Server side: (re)rolls this monster's stats against the players currently in the world.
    /// Runs on spawn and on every revive, so the field keeps up as players level.
    /// </summary>
    void ApplyLevel()
    {
        Kind kind = Kinds[kindIndex];
        int level = Mathf.Max(1, PlayerLevel() + kind.LevelOffset);

        MonsterName.Value = NetText.Trim64(kind.Name);
        Level.Value = level;
        MaxHp.Value = 20 + level * 26;
        Hp.Value = MaxHp.Value;
        Tint.Value = kind.Tint;
    }

    /// <summary>
    /// Out of combat and untouched, so it can quietly re-level. Without this the first batch
    /// of monsters would stay at the level of whoever was online when the server started.
    /// </summary>
    void RelevelIfIdle()
    {
        int wanted = Mathf.Max(1, PlayerLevel() + Kinds[kindIndex].LevelOffset);
        if (wanted != Level.Value && Hp.Value == MaxHp.Value)
        {
            ApplyLevel();
        }
    }

    /// <summary>Average level of the players in the world; 1 while nobody has spawned yet.</summary>
    int PlayerLevel()
    {
        int total = 0;
        int players = 0;

        foreach (var client in NetworkManager.ConnectedClientsList)
        {
            var playerObject = client.PlayerObject;
            var stats = playerObject != null ? playerObject.GetComponent<PlayerStats>() : null;
            if (stats != null)
            {
                total += stats.Level.Value;
                players++;
            }
        }

        return players == 0 ? 1 : Mathf.Max(1, Mathf.RoundToInt(total / (float)players));
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
            RelevelIfIdle();
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
        ApplyLevel();
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
        ApplyTint(BodyColor);
    }

    void OnLevelChanged(int previous, int current)
    {
        ApplyTint(BodyColor);
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
