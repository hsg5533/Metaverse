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
        new("슬라임", 0, new Color(0.42f, 0.82f, 0.45f)),
        new("고블린", 1, new Color(0.85f, 0.72f, 0.30f)),
        new("오크", 2, new Color(0.80f, 0.35f, 0.30f)),
    };

    /// <summary>One body per kind, indexed the same way as <see cref="Kinds"/> plus the boss.</summary>
    public GameObject[] Bodies;

    /// <summary>Flat ring that expands under the boss when it slams.</summary>
    public GameObject SlamRing;

    public float LungeDuration = 0.35f;
    public float SlamDuration = 0.6f;

    public const int BossShape = 3;

    /// <summary>Which body to show. Replicated, so late joiners see the right creature.</summary>
    public NetworkVariable<int> Shape = new(0, writePerm: NetworkVariableWritePermission.Server);

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
    public float LevelScale => Mathf.Min(1f + 0.04f * (Level.Value - 1), 1.8f) * (Shape.Value == BossShape ? 1.6f : 1f);
    public int ExpReward => 12 + Level.Value * 8;
    public int GoldReward => 4 + Level.Value * 3;

    public float HitFlashDuration = 0.25f;

    // Boss settings. A boss is the same monster with fatter numbers, a slam that catches
    // everyone standing near it, and a reward that goes to every player who helped.
    public const int BossLevelOffset = 5;
    public const int BossHpMultiplier = 8;
    public const int BossRewardMultiplier = 6;
    public const string BossName = "오우거 군주";

    public float SlamCooldown = 7f;
    public float SlamRadius = 6f;
    public float BossRespawnDelay = 180f;

    readonly System.Collections.Generic.Dictionary<ulong, PlayerStats> contributors = new();

    Renderer[] renderers;
    Collider[] colliders;
    CharacterController controller;
    float motionEndTime;
    bool motionIsSlam;
    bool motionPlaying;
    bool boss;
    int levelBonus;
    int kindIndex;
    float nextSlamTime;
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
        renderers = GetComponentsInChildren<Renderer>(true);
        colliders = GetComponentsInChildren<Collider>(true);
        controller = GetComponent<CharacterController>();
        home = transform.position;
        baseScale = transform.localScale;
    }

    public override void OnNetworkSpawn()
    {
        All.Add(this);
        Tint.OnValueChanged += OnTintChanged;
        Level.OnValueChanged += OnLevelChanged;
        Shape.OnValueChanged += OnShapeChanged;
        ApplyShape(Shape.Value);
        // Health is replicated to everyone, so the hit reaction needs no extra RPC.
        Hp.OnValueChanged += OnHpChanged;
        ApplyTint(BodyColor);
    }

    public override void OnNetworkDespawn()
    {
        All.Remove(this);
        Tint.OnValueChanged -= OnTintChanged;
        Level.OnValueChanged -= OnLevelChanged;
        Shape.OnValueChanged -= OnShapeChanged;
        Hp.OnValueChanged -= OnHpChanged;
    }

    void OnHpChanged(int previous, int current)
    {
        if (current < previous)
        {
            hitFlashEndTime = Time.time + HitFlashDuration;
        }
    }

    /// <summary>Attacks are decided on the server, so the motion is announced to everyone.</summary>
    [Rpc(SendTo.Everyone)]
    void AttackMotionRpc(bool slam)
    {
        motionIsSlam = slam;
        motionEndTime = Time.time + (slam ? SlamDuration : LungeDuration);
    }

    /// <summary>
    /// Local only. The bodies have no limb pivots, so the whole body lunges: a swipe forward
    /// for a normal hit, a rear-up and drop for the boss slam, with a ring marking its reach.
    /// </summary>
    void UpdateAttackMotion()
    {
        Transform body = ActiveBody();
        if (body == null)
        {
            return;
        }

        float remaining = motionEndTime - Time.time;
        if (remaining <= 0f)
        {
            if (motionPlaying)
            {
                motionPlaying = false;
                body.localPosition = Vector3.zero;
                body.localRotation = Quaternion.identity;
                if (SlamRing != null)
                {
                    SlamRing.SetActive(false);
                }
            }
            return;
        }

        motionPlaying = true;
        float duration = motionIsSlam ? SlamDuration : LungeDuration;
        float progress = Mathf.Clamp01(1f - remaining / duration);

        if (!motionIsSlam)
        {
            // One smooth swipe: out and back.
            float punch = Mathf.Sin(progress * Mathf.PI);
            body.localPosition = new Vector3(0f, 0f, punch * 0.45f);
            body.localRotation = Quaternion.Euler(punch * 22f, 0f, 0f);
            return;
        }

        // Rear up, drop, settle. Each phase is layered on top of the previous one.
        float windup = Mathf.Clamp01(progress / 0.4f);
        float strike = Mathf.Clamp01((progress - 0.4f) / 0.25f);
        float recover = Mathf.Clamp01((progress - 0.65f) / 0.35f);

        float pitch = Mathf.Lerp(0f, -26f, windup);
        pitch = Mathf.Lerp(pitch, 26f, strike);
        pitch = Mathf.Lerp(pitch, 0f, recover);

        float lift = Mathf.Lerp(0f, 0.45f, windup);
        lift = Mathf.Lerp(lift, -0.12f, strike);
        lift = Mathf.Lerp(lift, 0f, recover);

        body.localPosition = new Vector3(0f, lift, 0f);
        body.localRotation = Quaternion.Euler(pitch, 0f, 0f);

        if (SlamRing == null)
        {
            return;
        }

        if (strike <= 0f)
        {
            SlamRing.SetActive(false);
            return;
        }

        // The ring grows to the real slam radius, so the danger zone is honest.
        SlamRing.SetActive(true);
        float diameter = Mathf.Lerp(1f, SlamRadius * 2f, Mathf.SmoothStep(0f, 1f, strike));
        SlamRing.transform.localScale = new Vector3(diameter, 0.02f, diameter);
    }

    Transform ActiveBody()
    {
        if (Bodies == null || Bodies.Length == 0)
        {
            return null;
        }

        return Bodies[Mathf.Clamp(Shape.Value, 0, Bodies.Length - 1)].transform;
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
    public void Configure(int kind, int extraLevels = 0, bool isBoss = false)
    {
        kindIndex = Mathf.Clamp(kind, 0, Kinds.Length - 1);
        levelBonus = extraLevels;
        boss = isBoss;

        Shape.Value = boss ? BossShape : kindIndex;

        if (boss)
        {
            AggroRange = 22f;
            LeashRange = 40f;
            MoveSpeed = 2.9f;
            RespawnDelay = BossRespawnDelay;
        }

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
        int offset = boss ? BossLevelOffset : kind.LevelOffset;
        int level = Mathf.Max(1, PlayerLevel() + offset + levelBonus);

        MonsterName.Value = NetText.Trim64(boss ? BossName : kind.Name);
        Level.Value = level;
        MaxHp.Value = (20 + level * 26) * (boss ? BossHpMultiplier : 1);
        Hp.Value = MaxHp.Value;
        Tint.Value = boss ? new Color(0.58f, 0.14f, 0.30f) : kind.Tint;
        contributors.Clear();
    }

    /// <summary>
    /// Out of combat and untouched, so it can quietly re-level. Without this the first batch
    /// of monsters would stay at the level of whoever was online when the server started.
    /// </summary>
    void RelevelIfIdle()
    {
        int wanted = Mathf.Max(1, PlayerLevel() + (boss ? BossLevelOffset : Kinds[kindIndex].LevelOffset) + levelBonus);
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

        if (attacker != null)
        {
            contributors[attacker.OwnerClientId] = attacker;
        }

        Hp.Value = Mathf.Max(0, Hp.Value - amount);
        if (Hp.Value > 0)
        {
            return;
        }

        reviveTime = Time.time + RespawnDelay;

        // A boss pays out to everyone who landed a hit, so helping is never a waste.
        if (boss)
        {
            foreach (var helper in contributors.Values)
            {
                if (helper != null)
                {
                    Reward(helper, BossRewardMultiplier);
                }
            }

            ChatSystem.Announce($"{BossName} Lv.{Level.Value} 토벌 성공! 참가자 {contributors.Count}명");
            TreasureChest.UnlockAll();
            contributors.Clear();
            return;
        }

        if (attacker != null)
        {
            Reward(attacker, 1);
        }
    }

    /// <summary>
    /// Pays out a kill. Party members standing nearby each receive the full experience and
    /// the full gold, plus the quest tick, so hunting together is strictly better than alone.
    /// </summary>
    void Reward(PlayerStats player, int multiplier)
    {
        var receivers = PartySystem.Instance != null
            ? PartySystem.Instance.Share(player)
            : new System.Collections.Generic.List<PlayerStats> { player };

        int exp = ExpReward * multiplier;
        int gold = GoldReward * multiplier;

        foreach (var receiver in receivers)
        {
            if (receiver == null)
            {
                continue;
            }

            receiver.GainReward(exp, gold);

            var quests = receiver.GetComponent<PlayerQuests>();
            if (quests != null)
            {
                quests.OnMonsterKilled();
            }
        }
    }

    /// <summary>Server side: the boss sweep, which is why standing in a clump hurts.</summary>
    void Slam()
    {
        nextSlamTime = Time.time + SlamCooldown;
        AttackMotionRpc(true);

        foreach (var client in NetworkManager.ConnectedClientsList)
        {
            var playerObject = client.PlayerObject;
            if (playerObject == null || Vector3.Distance(playerObject.transform.position, transform.position) > SlamRadius)
            {
                continue;
            }

            var stats = playerObject.GetComponent<PlayerStats>();
            if (stats != null)
            {
                stats.TakeDamage(Damage * 2, $"{BossName}의 내리찍기");
            }
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
        UpdateAttackMotion();

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

        if (boss && Time.time >= nextSlamTime)
        {
            Slam();
            return;
        }

        if (Time.time >= nextAttackTime)
        {
            nextAttackTime = Time.time + AttackCooldown;
            AttackMotionRpc(false);
            target.TakeDamage(Damage, $"{MonsterName.Value} Lv.{Level.Value}");
        }
    }

    void Revive()
    {
        ApplyLevel();
        Teleport(home);
    }

    /// <summary>The controller owns the transform, so it has to be switched off to move it.</summary>
    void Teleport(Vector3 position)
    {
        if (controller != null)
        {
            controller.enabled = false;
            transform.position = position;
            controller.enabled = true;
            return;
        }

        transform.position = position;
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

        Vector3 motion = step.normalized * MoveSpeed * Time.deltaTime;

        if (controller != null && controller.enabled)
        {
            // The controller slides along walls instead of walking through them, which is
            // what the dungeon corridor needs. The downward push keeps it on the floor.
            controller.Move(motion + Vector3.down * 4f * Time.deltaTime);
        }
        else
        {
            Vector3 position = transform.position + motion;
            position.y = home.y;
            transform.position = position;
        }

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

    void OnShapeChanged(int previous, int current)
    {
        ApplyShape(current);
    }

    /// <summary>Shows one body and hides the rest, so a Goblin never looks like a Slime.</summary>
    void ApplyShape(int shape)
    {
        if (Bodies == null || Bodies.Length == 0)
        {
            return;
        }

        for (int i = 0; i < Bodies.Length; i++)
        {
            if (Bodies[i] != null)
            {
                Bodies[i].SetActive(i == shape);
            }
        }

        NameTagHeight = shape switch
        {
            0 => 1.5f,
            1 => 2.0f,
            2 => 2.5f,
            _ => 3.6f,
        };

        ApplyTint(BodyColor);
    }

    /// <summary>Parts named like this keep their own colour: eyes, tusks, horns, claws.</summary>
    static readonly string[] UntintedParts = { "Eye", "Tusk", "Horn", "Claw", "Detail", "Ring" };

    void ApplyTint(Color color)
    {
        foreach (var renderer in renderers)
        {
            if (renderer == null || !renderer.gameObject.activeInHierarchy || IsUntinted(renderer.name))
            {
                continue;
            }

            renderer.material.color = color;
        }
    }

    static bool IsUntinted(string name)
    {
        foreach (string keyword in UntintedParts)
        {
            if (name.Contains(keyword))
            {
                return true;
            }
        }

        return false;
    }

    void OnGUI()
    {
        MetaverseUi.ApplyFont();

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
