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
    /// One kind of monster: what it is called, how far above the players' level it rolls,
    /// and its colour. Which kinds appear where is decided by Rosters.
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
        new("서리 늑대", 1, new Color(0.72f, 0.80f, 0.90f)),
        new("설원 망령", 2, new Color(0.66f, 0.72f, 0.94f)),
        new("얼음 골렘", 3, new Color(0.58f, 0.74f, 0.86f)),
        new("마그마 두꺼비", 1, new Color(0.80f, 0.34f, 0.18f)),
        new("용암 전갈", 2, new Color(0.74f, 0.26f, 0.14f)),
        new("잿불 정령", 3, new Color(1f, 0.55f, 0.20f)),
    };

    /// <summary>
    /// Which kinds live on which ground, lined up with <see cref="Bosses"/>. A frost field
    /// and a lava field share nothing, so walking into one is walking into new creatures.
    /// </summary>
    public static readonly int[][] Rosters =
    {
        new[] { 0, 1, 2 },
        new[] { 3, 4, 5 },
        new[] { 6, 7, 8 },
    };

    public static int[] RosterFor(int theme)
    {
        return Rosters[Mathf.Clamp(theme, 0, Rosters.Length - 1)];
    }

    /// <summary>
    /// The boss of each theme, lined up with <see cref="Rosters"/>: one per ground, so a
    /// dungeon's fight looks like the place it happens in. Same shape as any other kind,
    /// it just carries the boss level offset.
    /// </summary>
    public static readonly Kind[] Bosses =
    {
        new("오우거 군주", BossLevelOffset, new Color(0.58f, 0.14f, 0.30f)),
        new("서리 거인", BossLevelOffset, new Color(0.46f, 0.66f, 0.88f)),
        new("용암 파괴자", BossLevelOffset, new Color(0.72f, 0.22f, 0.10f)),
    };

    /// <summary>The boss body for a theme sits after the kinds in <see cref="Bodies"/>.</summary>
    public static int BossShapeFor(int theme)
    {
        return Kinds.Length + Mathf.Clamp(theme, 0, Bosses.Length - 1);
    }

    public static string BossNameFor(int theme)
    {
        return Bosses[Mathf.Clamp(theme, 0, Bosses.Length - 1)].Name;
    }

    /// <summary>One body per kind in <see cref="Kinds"/> order, then one per boss.</summary>
    public GameObject[] Bodies;

    /// <summary>Flat ring that expands under the boss when it slams.</summary>
    public GameObject SlamRing;

    public float LungeDuration = 0.35f;
    public float SlamDuration = 0.6f;

    /// <summary>Which body to show. Replicated, so late joiners see the right creature.</summary>
    public NetworkVariable<int> Shape = new(0, writePerm: NetworkVariableWritePermission.Server);

    public float MoveSpeed = 2.4f;
    public float AggroRange = 12f;
    public float AttackRange = 1.9f;
    public float AttackCooldown = 1.6f;
    public float LeashRange = 26f;
    public float RespawnDelay = 6f;
    public float NameTagHeight = 1.8f;

    public NetworkVariable<FixedString64Bytes> MonsterName = new(
        new FixedString64Bytes("Slime"),
        writePerm: NetworkVariableWritePermission.Server
    );

    public NetworkVariable<int> Level = new(1, writePerm: NetworkVariableWritePermission.Server);
    public NetworkVariable<int> Hp = new(40, writePerm: NetworkVariableWritePermission.Server);
    public NetworkVariable<int> MaxHp = new(40, writePerm: NetworkVariableWritePermission.Server);
    public NetworkVariable<Color> Tint = new(
        new Color(0.4f, 0.8f, 0.4f),
        writePerm: NetworkVariableWritePermission.Server
    );

    public bool IsAlive => Hp.Value > 0;

    // Damage grows faster than the players' defence (+2 a level), so higher level monsters
    // actually hurt more instead of being shrugged off.
    public int Damage => 6 + Level.Value * 4;

    /// <summary>Higher level monsters are visibly bigger, so a tough one reads at a glance.</summary>
    public float LevelScale =>
        Mathf.Min(1f + 0.04f * (Level.Value - 1), 1.8f) * (IsBossShape ? 1.6f : 1f);

    /// <summary>True while one of the boss bodies is the one on show.</summary>
    public bool IsBossShape => Shape.Value >= Kinds.Length;
    public int ExpReward => 12 + Level.Value * 8;
    public int GoldReward => 4 + Level.Value * 3;

    public float HitFlashDuration = 0.25f;

    // Boss settings. A boss is the same monster with fatter numbers, a slam that catches
    // everyone standing near it, and a reward that goes to every player who helped.
    public const int BossLevelOffset = 5;
    public const int BossHpMultiplier = 8;
    public const int BossRewardMultiplier = 6;

    public float SlamCooldown = 7f;
    public float SlamRadius = 6f;
    public float BossRespawnDelay = 180f;

    readonly System.Collections.Generic.Dictionary<ulong, PlayerStats> contributors = new();

    Renderer[] renderers;
    Renderer[] tintTargets = System.Array.Empty<Renderer>();
    CharacterController controller;
    float motionEndTime;
    bool motionIsSlam;
    bool motionPlaying;
    readonly List<WalkPart> walkParts = new();
    bool walkPartsAreLegs;
    float hoverHeight;
    float walkCycle;
    float walkBlend;
    Vector3 lastPosition;
    bool boss;
    int levelBonus;
    int themeIndex;
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

    string tag;
    int taggedLevel = int.MinValue;
    FixedString64Bytes taggedName;

    void Awake()
    {
        renderers = GetComponentsInChildren<Renderer>(true);
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
        if (current >= previous)
        {
            return;
        }

        hitFlashEndTime = Time.time + HitFlashDuration;
        GameSound.Play(current > 0 ? GameSound.Hit : GameSound.Death, transform.position);
    }

    /// <summary>
    /// The exact amount just dealt, announced separately from Hp itself: re-levelling while
    /// idle also moves Hp (see RelevelIfIdle) and that is not a hit, so diffing Hp.OnValueChanged
    /// would show a false number every time the field quietly re-scales to the players' level.
    /// </summary>
    [Rpc(SendTo.Everyone)]
    void DamageNumberRpc(int amount)
    {
        DamageNumbers.Add(transform.position + Vector3.up * NameTagHeight, amount);
    }

    /// <summary>Attacks are decided on the server, so the motion is announced to everyone.</summary>
    [Rpc(SendTo.Everyone)]
    void AttackMotionRpc(bool slam)
    {
        motionIsSlam = slam;
        motionEndTime = Time.time + (slam ? SlamDuration : LungeDuration);
        GameSound.Play(slam ? GameSound.Death : GameSound.Growl, transform.position);
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

    /// <summary>
    /// How far each body floats above the ground, per shape. Zero means it walks; anything
    /// else never touches down, so a ghost drifts and a bat stays in the air.
    /// </summary>
    static readonly float[] HoverHeights =
    {
        0f,
        0f,
        0f, // slime, goblin, orc
        0f,
        0.5f,
        0f, // wolf, wraith, ice golem
        0f,
        0f,
        0.45f, // toad, scorpion, wisp
        0f,
        0f,
        0f, // the three bosses
    };

    /// <summary>A limb that swings while the monster walks, and where it swings about.</summary>
    struct WalkPart
    {
        public Transform Part;
        public Vector3 BasePosition;
        public Quaternion BaseRotation;

        /// <summary>Local point the limb turns around: the top of it, so it swings from the hip.</summary>
        public Vector3 Pivot;

        public Vector3 Axis;
        public float Phase;
        public float Swing;
    }

    /// <summary>
    /// Local only: swings whatever the active body has - legs, arms, wings - in time with how
    /// fast it is actually moving. Bodies with no legs bob instead, so slimes hop and the
    /// floating ones drift up and down.
    /// The position is replicated, so this runs the same on every client without an RPC.
    /// </summary>
    void UpdateWalkMotion()
    {
        Transform body = ActiveBody();
        if (body == null)
        {
            return;
        }

        // Off screen there is nobody to see the legs move; the pose catches up on the first
        // frame it comes back into view.
        if (tintTargets.Length > 0 && !tintTargets[0].isVisible)
        {
            lastPosition = transform.position;
            return;
        }

        float delta = Time.deltaTime;
        float speed =
            delta > 0f
                ? Vector3.Distance(FlatPosition(transform.position), FlatPosition(lastPosition))
                    / delta
                : 0f;
        lastPosition = transform.position;

        if (hoverHeight > 0f)
        {
            // Nothing that hovers ever holds still: it drifts on the spot and beats harder
            // as it closes in.
            walkBlend = 1f;
            walkCycle += (3.4f + Mathf.Min(speed, 6f)) * delta * 2.2f;
        }
        else
        {
            walkBlend = Mathf.MoveTowards(walkBlend, speed > 0.4f ? 1f : 0f, delta * 6f);

            // The cycle runs off distance covered, so a fast monster takes faster steps.
            walkCycle += Mathf.Min(speed, 8f) * delta * 3.4f;
        }

        foreach (var part in walkParts)
        {
            if (part.Part == null)
            {
                continue;
            }

            float angle = Mathf.Sin(walkCycle + part.Phase) * part.Swing * walkBlend;
            Quaternion swing = Quaternion.AngleAxis(angle, part.Axis);
            part.Part.localRotation = swing * part.BaseRotation;
            part.Part.localPosition = part.Pivot + swing * (part.BasePosition - part.Pivot);
        }

        if (!walkPartsAreLegs)
        {
            // Nothing to step with: rise and fall on the spot instead, and a winged one sits
            // up off the ground the whole time. Only the height is touched, so an attack
            // lunge still carries the body forward underneath it.
            float height =
                hoverHeight > 0f
                    ? hoverHeight + Mathf.Sin(walkCycle) * 0.16f
                    : Mathf.Abs(Mathf.Sin(walkCycle)) * 0.22f * walkBlend;

            Vector3 position = body.localPosition;
            body.localPosition = new Vector3(position.x, height, position.z);
        }
    }

    /// <summary>Picks out the limbs of the body now on show; called whenever the shape changes.</summary>
    void CollectWalkParts(Transform body, int shape)
    {
        walkParts.Clear();
        walkPartsAreLegs = false;
        hoverHeight = HoverHeights[Mathf.Clamp(shape, 0, HoverHeights.Length - 1)];
        walkCycle = 0f;
        walkBlend = 0f;

        if (body == null)
        {
            return;
        }

        foreach (var child in body.GetComponentsInChildren<Transform>())
        {
            // Trim and cracks are named Detail...; they ride on a limb, they are not one.
            bool decoration = child.name.StartsWith("Detail");
            bool leg = !decoration && child.name.Contains("Leg");
            bool arm = !decoration && (child.name.Contains("Arm") || child.name.Contains("Sleeve"));
            if (!leg && !arm)
            {
                continue;
            }

            Vector3 position = child.localPosition;

            // Opposite corners move together: left leg with right arm, front left paw with
            // back right paw.
            float phase = (position.x < 0f) ^ (position.z < 0f) ? 0f : Mathf.PI;
            if (arm)
            {
                phase += Mathf.PI;
            }

            walkParts.Add(
                new WalkPart
                {
                    Part = child,
                    BasePosition = position,
                    BaseRotation = child.localRotation,
                    Pivot = position + Vector3.up * child.localScale.y * 0.5f,
                    Axis = Vector3.right,
                    Phase = phase,
                    Swing = leg ? 26f : 14f,
                }
            );

            walkPartsAreLegs |= leg;
        }

        AttachHands(body);
    }

    /// <summary>
    /// Hands, fists and whatever they hold are separate parts sitting where a limb ends, not
    /// children of it. Give each one the swing of the nearest limb so it travels with the
    /// arm instead of hanging in the air while the arm moves off.
    /// </summary>
    void AttachHands(Transform body)
    {
        int limbCount = walkParts.Count;
        if (limbCount == 0)
        {
            return;
        }

        foreach (var child in body.GetComponentsInChildren<Transform>())
        {
            // Claws keep their own colour, paws and feet take the body's, which is why both
            // spellings exist. Either way they hang off a limb and travel with it.
            if (!NameHas(child.name, "Claw", "Paw", "Foot"))
            {
                continue;
            }

            Vector3 position = child.localPosition;

            // Nearest limb, but only if it is close enough to be the thing it hangs off -
            // a scorpion's stinger is nowhere near a leg and must stay where it is.
            int nearest = -1;
            float best = 1f;
            for (int i = 0; i < limbCount; i++)
            {
                float distance = Vector3.Distance(walkParts[i].BasePosition, position);
                if (distance < best)
                {
                    best = distance;
                    nearest = i;
                }
            }

            if (nearest < 0)
            {
                continue;
            }

            WalkPart limb = walkParts[nearest];
            walkParts.Add(
                new WalkPart
                {
                    Part = child,
                    BasePosition = position,
                    BaseRotation = child.localRotation,

                    // The limb's pivot, so the pair turns as one piece.
                    Pivot = limb.Pivot,
                    Axis = limb.Axis,
                    Phase = limb.Phase,
                    Swing = limb.Swing,
                }
            );
        }
    }

    /// <summary>Local only: flash white and squash for a moment after being hit.</summary>
    void UpdateHitFlash()
    {
        Vector3 bodyScale = baseScale * LevelScale;
        float remaining = hitFlashEndTime - Time.time;

        if (remaining <= 0f)
        {
            // Only when it actually differs: assigning a transform every frame dirties the
            // whole hierarchy, and most of these monsters are standing still.
            if (transform.localScale != bodyScale)
            {
                transform.localScale = bodyScale;
            }

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
        transform.localScale = Vector3.Scale(
            bodyScale,
            new Vector3(1f + 0.2f * strength, 1f - 0.2f * strength, 1f + 0.2f * strength)
        );
    }

    /// <summary>The kind's colour, deepened as the monster levels so tough ones look tough.</summary>
    Color BodyColor =>
        Color.Lerp(Tint.Value, Tint.Value * 0.45f, Mathf.Min((Level.Value - 1) * 0.06f, 1f));

    /// <summary>Server side: give the freshly spawned monster its kind, stats and home point.</summary>
    public void Configure(int kind, int extraLevels = 0, bool isBoss = false, int theme = 0)
    {
        kindIndex = Mathf.Clamp(kind, 0, Kinds.Length - 1);
        levelBonus = extraLevels;
        boss = isBoss;
        themeIndex = Mathf.Clamp(theme, 0, Rosters.Length - 1);

        Shape.Value = boss ? BossShapeFor(themeIndex) : kindIndex;

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
        Kind kind = CurrentKind;
        int level = WantedLevel;

        MonsterName.Value = NetText.Trim64(kind.Name);
        Level.Value = level;
        MaxHp.Value = (20 + level * 26) * (boss ? BossHpMultiplier : 1);
        Hp.Value = MaxHp.Value;
        Tint.Value = kind.Tint;
        contributors.Clear();
    }

    /// <summary>What this monster is: its boss entry, or its entry in the kind table.</summary>
    Kind CurrentKind => boss ? Bosses[themeIndex] : Kinds[kindIndex];

    /// <summary>The level it should be right now, given who is online.</summary>
    int WantedLevel => Mathf.Max(1, PlayerLevel() + CurrentKind.LevelOffset + levelBonus);

    /// <summary>
    /// Out of combat and untouched, so it can quietly re-level. Without this the first batch
    /// of monsters would stay at the level of whoever was online when the server started.
    /// </summary>
    void RelevelIfIdle()
    {
        if (WantedLevel != Level.Value && Hp.Value == MaxHp.Value)
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

        DamageNumberRpc(amount);
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

            ChatSystem.Announce(
                $"{MonsterName.Value} Lv.{Level.Value} 토벌 성공! 참가자 {contributors.Count}명"
            );
            TreasureChest.UnlockNear(transform.position);
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
        var receivers =
            PartySystem.Instance != null
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
            if (
                playerObject == null
                || Vector3.Distance(playerObject.transform.position, transform.position)
                    > SlamRadius
            )
            {
                continue;
            }

            var stats = playerObject.GetComponent<PlayerStats>();
            if (stats != null)
            {
                stats.TakeDamage(Damage * 2, $"{MonsterName.Value}의 내리찍기");
            }
        }
    }

    /// <summary>Server side: closest living monster inside the attacker's cone.</summary>
    public static Monster FindTarget(
        Vector3 origin,
        Vector3 forward,
        float range,
        float coneDegrees
    )
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
        TrimBodies();
        SyncVisuals();
        UpdateHitFlash();
        UpdateAttackMotion();
        UpdateWalkMotion();

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
        if (
            Vector3.Distance(FlatPosition(targetPosition), FlatPosition(transform.position))
            > AttackRange
        )
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
            transform.rotation = Quaternion.Slerp(
                transform.rotation,
                Quaternion.LookRotation(direction),
                8f * Time.deltaTime
            );
        }
    }

    static Vector3 FlatPosition(Vector3 position)
    {
        position.y = 0f;
        return position;
    }

    bool bodiesTrimmed;

    /// <summary>
    /// The prefab holds every body so one network prefab covers every creature, which leaves
    /// a spawned monster carrying eleven it will never show - about 240 objects of dead
    /// weight each, and there are fifty of them.
    /// The shape is decided once, in Configure, and never changes after; waiting a frame lets
    /// that value arrive on the clients too.
    /// </summary>
    void TrimBodies()
    {
        if (bodiesTrimmed || Bodies == null || !IsSpawned)
        {
            return;
        }

        bodiesTrimmed = true;

        for (int i = 0; i < Bodies.Length; i++)
        {
            if (i != Shape.Value && Bodies[i] != null)
            {
                Destroy(Bodies[i]);
            }
        }

        // Only the body that stayed. Collecting the whole hierarchy again would pick the
        // doomed ones straight back up: Destroy does not take effect until the end of the
        // frame, and from the next frame those references throw when touched.
        Transform body = ActiveBody();
        renderers =
            body != null
                ? body.GetComponentsInChildren<Renderer>(true)
                : System.Array.Empty<Renderer>();
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
            if (renderer != null)
            {
                renderer.enabled = alive;
            }
        }

        // The only collider is the controller on the root; the bodies are built without any.
        if (controller != null)
        {
            controller.enabled = alive;
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

        NameTagHeight = TagHeights[Mathf.Clamp(shape, 0, TagHeights.Length - 1)];

        CollectWalkParts(ActiveBody(), shape);
        CollectTintTargets(ActiveBody());
        ApplyTint(BodyColor);
    }

    /// <summary>Where the name floats, per body, since they are nowhere near the same size.</summary>
    static readonly float[] TagHeights =
    {
        1.5f,
        2.0f,
        2.5f, // slime, goblin, orc
        1.6f,
        2.9f,
        2.9f, // wolf, wraith, ice golem
        1.4f,
        1.5f,
        2.75f, // toad, scorpion, wisp
        3.6f,
        4.0f,
        3.4f, // the three bosses
    };

    /// <summary>Parts named like this keep their own colour: eyes, tusks, horns, claws.</summary>
    static readonly string[] UntintedParts = { "Eye", "Tusk", "Horn", "Claw", "Detail", "Ring" };

    static MaterialPropertyBlock tintBlock;
    static readonly int BaseColor = Shader.PropertyToID("_BaseColor");

    /// <summary>
    /// Through a property block rather than renderer.material: touching that clones the
    /// material, and a cloned material batches with nothing. Fifty monsters of fifteen parts
    /// each is fifty times fifteen draw calls the phone did not need.
    /// </summary>
    void ApplyTint(Color color)
    {
        tintBlock ??= new MaterialPropertyBlock();
        tintBlock.SetColor(BaseColor, color);

        foreach (var renderer in tintTargets)
        {
            renderer.SetPropertyBlock(tintBlock);
        }
    }

    /// <summary>
    /// The parts of the body on show that take the monster's colour. Collected when the shape
    /// changes: a hit flash tints every frame, and the prefab carries a dozen bodies worth of
    /// renderers to sift through.
    /// </summary>
    void CollectTintTargets(Transform body)
    {
        var found = new List<Renderer>();
        if (body != null)
        {
            foreach (var renderer in body.GetComponentsInChildren<Renderer>(true))
            {
                if (!NameHas(renderer.name, UntintedParts))
                {
                    found.Add(renderer);
                }
            }
        }

        tintTargets = found.ToArray();
    }

    static bool NameHas(string name, params string[] keywords)
    {
        foreach (string keyword in keywords)
        {
            if (name.Contains(keyword))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Every monster's tag, drawn from one place. As an OnGUI of its own this ran twice a
    /// frame per monster, and there are fifty of them standing around six fields.
    /// </summary>
    public static void DrawTags()
    {
        var camera = Camera.main;
        if (camera == null)
        {
            return;
        }

        nameTagStyle ??= new GUIStyle(GUI.skin.label)
        {
            alignment = TextAnchor.MiddleCenter,
            clipping = TextClipping.Overflow,
        };

        foreach (var monster in All)
        {
            if (monster != null)
            {
                monster.DrawTag(camera);
            }
        }
    }

    /// <summary>
    /// The label over its head, built once and kept. OnGUI runs several times a frame and a
    /// dozen monsters can be on screen, so building this each time is a hundred strings a
    /// frame to collect for a line that changes when a monster levels or respawns as another
    /// kind, and at no other time.
    /// </summary>
    string Tag()
    {
        if (tag == null || taggedLevel != Level.Value || !taggedName.Equals(MonsterName.Value))
        {
            taggedLevel = Level.Value;
            taggedName = MonsterName.Value;
            tag = $"{taggedName} Lv.{taggedLevel}";
        }

        return tag;
    }

    void DrawTag(Camera camera)
    {
        // Off screen there is nothing to label, and the areas are open enough that a monster
        // a hundred metres away is still inside the frustum: the village would read every tag
        // in the hunting fields.
        if (!IsSpawned || !IsAlive || tintTargets.Length == 0 || !tintTargets[0].isVisible)
        {
            return;
        }

        if (Vector3.Distance(camera.transform.position, transform.position) > 40f)
        {
            return;
        }

        Vector3 screenPoint = MetaverseUi.ScreenPoint(
            camera,
            transform.position + Vector3.up * NameTagHeight
        );
        if (screenPoint.z <= 0f)
        {
            return;
        }

        float x = screenPoint.x;
        float y = MetaverseUi.Height - screenPoint.y;

        GUI.Label(new Rect(x - 110f, y - 20f, 220f, 20f), Tag(), nameTagStyle);

        const float barWidth = 60f;
        float fill = MaxHp.Value > 0 ? Hp.Value / (float)MaxHp.Value : 0f;
        MetaverseUi.Bar(
            new Rect(x - barWidth * 0.5f, y, barWidth, 5f),
            fill,
            new Color(0.85f, 0.25f, 0.25f, 0.95f)
        );
    }
}
