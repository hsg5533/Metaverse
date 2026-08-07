using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Fishing: cast the float, wait for it to go under, and click while it is. The waiting is
/// the point, so the only skill asked for is noticing the bite before it lets go.
/// The owner drives it and the server pays out, the same way an attack works.
/// </summary>
[RequireComponent(typeof(PlayerGear))]
public class PlayerFishing : NetworkBehaviour
{
    /// <summary>
    /// How big each kind runs, in the order they sit in PlayerGear.Pieces. The fish itself
    /// goes into the bag; the size is what the experience and the record are made of.
    /// </summary>
    public static readonly (int Small, int Large)[] Sizes =
    {
        (12, 40),
        (25, 70),
        (30, 95),
        (20, 60),
        (40, 110),
    };

    public float CastRange = 7f;
    public float BiteWindow = 1.4f;

    /// <summary>The float, made of two cubes when the first cast happens.</summary>
    Transform bobber;

    enum Phase { Idle, Casting, Waiting, Biting }

    Phase phase = Phase.Idle;
    float phaseEnds;
    Vector3 bobberTarget;
    PlayerGear gear;
    AvatarLimbAnimator limbAnimator;

    /// <summary>The rod is out and there is water in front: the attack button is ours.</summary>
    public bool Ready => gear != null && gear.HoldingRod && FishingSpot.NearAny(transform.position);

    void Awake()
    {
        gear = GetComponent<PlayerGear>();
        limbAnimator = GetComponent<AvatarLimbAnimator>();
    }

    void Update()
    {
        if (!IsOwner || !IsSpawned)
        {
            return;
        }

        if (!Ready)
        {
            Stow();
            return;
        }

        if (Pressed())
        {
            Click();
        }

        Advance();
    }

    static bool Pressed()
    {
        if (ChatSystem.IsTyping || MetaverseUi.WindowOpen || MetaverseHUD.PointerOverHud)
        {
            return false;
        }

        var mouse = Mouse.current;
        return (mouse != null && mouse.leftButton.wasPressedThisFrame) || MobileInput.ConsumeAttack();
    }

    /// <summary>One button does the lot: cast, then strike, and anything else reels in.</summary>
    void Click()
    {
        switch (phase)
        {
            case Phase.Idle:
                Cast();
                break;

            case Phase.Biting:
                CatchRpc(bobberTarget);
                Stow();
                break;

            default:
                ChatSystem.Local("놓쳤습니다.");
                Stow();
                break;
        }
    }

    void Cast()
    {
        bobberTarget = transform.position + transform.forward * CastRange;
        // On the surface, not in it: the float is 16cm of cube and half of it should show.
        bobberTarget.y = FishingSpot.WaterHeight + 0.08f;

        // Where it lands is what matters, not where the angler stands.
        if (!FishingSpot.OnWater(bobberTarget))
        {
            ChatSystem.Local("물 쪽을 보고 던지세요.");
            return;
        }

        phase = Phase.Casting;
        phaseEnds = Time.time + 0.45f;

        // The arm swings the same way it does for a sword; the rod is in the same hand.
        if (limbAnimator != null)
        {
            limbAnimator.PlayAttack();
        }

        GameSound.Play(GameSound.Cast, transform.position);
    }

    void Advance()
    {
        if (phase == Phase.Idle || Time.time < phaseEnds)
        {
            Float();
            return;
        }

        switch (phase)
        {
            case Phase.Casting:
                // The wait is the game: long enough to look away, short enough to come back.
                phase = Phase.Waiting;
                phaseEnds = Time.time + Random.Range(2.5f, 8f);
                break;

            case Phase.Waiting:
                phase = Phase.Biting;
                phaseEnds = Time.time + BiteWindow;
                GameSound.Play(GameSound.Bite, bobberTarget);
                break;

            default:
                ChatSystem.Local("물고기가 미끼를 물고 달아났습니다.");
                Stow();
                break;
        }

        Float();
    }

    /// <summary>The float flies out, then bobs, then goes under when something takes it.</summary>
    void Float()
    {
        if (phase == Phase.Idle)
        {
            return;
        }

        if (bobber == null)
        {
            bobber = Build();
        }

        Vector3 place = bobberTarget;
        if (phase == Phase.Casting)
        {
            // An arc from the rod to the water, so the cast is something you watch.
            float t = 1f - (phaseEnds - Time.time) / 0.45f;
            place = Vector3.Lerp(transform.position + Vector3.up * 1.4f, bobberTarget, t);
            place.y += Mathf.Sin(t * Mathf.PI) * 2.2f;
        }
        else if (phase == Phase.Waiting)
        {
            place.y += Mathf.Sin(Time.time * 2.2f) * 0.06f;
        }
        else
        {
            // Pulled under, but not so far it disappears through an opaque surface.
            place.y -= 0.13f;
        }

        bobber.position = place;
        bobber.gameObject.SetActive(true);
    }

    static Transform Build()
    {
        var root = new GameObject("Bobber") { hideFlags = HideFlags.HideInHierarchy };
        var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");

        Part(root.transform, new Vector3(0f, 0.08f, 0f), new Vector3(0.16f, 0.16f, 0.16f),
            new Material(shader) { color = new Color(0.90f, 0.25f, 0.22f) });
        Part(root.transform, new Vector3(0f, -0.06f, 0f), new Vector3(0.13f, 0.13f, 0.13f),
            new Material(shader) { color = Color.white });

        return root.transform;
    }

    static void Part(Transform parent, Vector3 position, Vector3 scale, Material material)
    {
        var part = GameObject.CreatePrimitive(PrimitiveType.Cube);
        Destroy(part.GetComponent<Collider>());
        part.transform.SetParent(parent, false);
        part.transform.localPosition = position;
        part.transform.localScale = scale;
        part.GetComponent<Renderer>().sharedMaterial = material;
    }

    void Stow()
    {
        phase = Phase.Idle;
        if (bobber != null)
        {
            bobber.gameObject.SetActive(false);
        }
    }

    public override void OnNetworkDespawn()
    {
        if (bobber != null)
        {
            Destroy(bobber.gameObject);
        }
    }

    /// <summary>Server side: rolls what was on the end of it and pays for it.</summary>
    [Rpc(SendTo.Server)]
    void CatchRpc(Vector3 float3, RpcParams rpcParams = default)
    {
        // The float has to be on water and within a cast of whoever claims to be holding it.
        if (rpcParams.Receive.SenderClientId != OwnerClientId
            || !FishingSpot.OnWater(float3)
            || Vector3.Distance(transform.position, float3) > CastRange + 3f)
        {
            return;
        }

        int kind = Random.Range(0, Sizes.Length);
        int size = Random.Range(Sizes[kind].Small, Sizes[kind].Large + 1);
        int piece = PlayerGear.FirstFish + kind;

        var stats = GetComponent<PlayerStats>();
        if (stats != null)
        {
            stats.GainReward(size, 0);
            stats.RecordFish(size);
        }

        gear.Give(piece);
        NoticeRpc(NetText.Trim512($"{PlayerGear.Pieces[piece].Name} {size}cm!"));
    }

    [Rpc(SendTo.Owner)]
    void NoticeRpc(FixedString512Bytes text)
    {
        ChatSystem.Local(text.ToString());
        GameSound.PlayLocal(GameSound.Pickup);
    }

    void OnGUI()
    {
        if (!IsOwner || !IsSpawned || !Ready)
        {
            return;
        }

        MetaverseUi.ApplyFont();

        string text = phase switch
        {
            Phase.Idle => "[공격] 찌 던지기",
            Phase.Waiting => "기다리는 중...",
            Phase.Biting => "<b>입질! 지금 클릭</b>",
            _ => "",
        };

        if (text.Length > 0)
        {
            var rect = new Rect(MetaverseUi.Width * 0.5f - 120f, MetaverseUi.Height * 0.5f + 60f, 240f, 26f);
            GUI.Label(rect, text, MetaverseUi.Centered);
        }
    }
}
