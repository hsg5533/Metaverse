using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Village shopkeeper. Purely local UI: it shows a prompt when the local avatar is close
/// and sends the purchase to the server through <see cref="PlayerStats"/>.
/// </summary>
public class ShopNpc : MonoBehaviour
{
    /// <summary>True while any shop window is open, so the attack input stays quiet.</summary>
    public static bool PanelOpen;

    public string ShopName = "Village Shop";
    public float InteractRange = 3.5f;
    public float PromptHeight = 2.4f;

    bool open;

    void OnDisable()
    {
        Close();
    }

    void Update()
    {
        var stats = PlayerAvatar.Local != null ? PlayerAvatar.Local.GetComponent<PlayerStats>() : null;
        if (stats == null || Vector3.Distance(PlayerAvatar.Local.transform.position, transform.position) > InteractRange)
        {
            Close();
            return;
        }

        var keyboard = Keyboard.current;
        if (keyboard != null && keyboard.eKey.wasPressedThisFrame && !ChatSystem.IsTyping)
        {
            open = !open;
            PanelOpen = open;
        }
    }

    void Close()
    {
        if (open)
        {
            open = false;
            PanelOpen = false;
        }
    }

    void OnGUI()
    {
        var stats = PlayerAvatar.Local != null ? PlayerAvatar.Local.GetComponent<PlayerStats>() : null;
        if (stats == null)
        {
            return;
        }

        if (open)
        {
            DrawShop(stats);
            return;
        }

        var camera = Camera.main;
        if (camera == null || Vector3.Distance(PlayerAvatar.Local.transform.position, transform.position) > InteractRange)
        {
            return;
        }

        Vector3 screenPoint = camera.WorldToScreenPoint(transform.position + Vector3.up * PromptHeight);
        if (screenPoint.z <= 0f)
        {
            return;
        }

        var prompt = new GUIContent($"[E] {ShopName}");
        Vector2 size = GUI.skin.box.CalcSize(prompt);
        GUI.Box(new Rect(screenPoint.x - size.x * 0.5f, Screen.height - screenPoint.y, size.x, size.y), prompt);
    }

    void DrawShop(PlayerStats stats)
    {
        var area = new Rect(Screen.width * 0.5f - 150f, Screen.height * 0.5f - 90f, 300f, 180f);
        GUILayout.BeginArea(area, GUI.skin.box);

        GUILayout.Label($"<b>{ShopName}</b>", RichLabel());
        GUILayout.Label($"Gold: {stats.Gold.Value}");
        GUILayout.Space(6);

        if (GUILayout.Button($"Weapon Lv.{stats.WeaponLevel.Value} -> Lv.{stats.WeaponLevel.Value + 1}  ({stats.WeaponPrice} G)  +4 ATK"))
        {
            stats.BuyWeaponRpc();
        }

        if (GUILayout.Button($"Armor Lv.{stats.ArmorLevel.Value} -> Lv.{stats.ArmorLevel.Value + 1}  ({stats.ArmorPrice} G)  +3 DEF"))
        {
            stats.BuyArmorRpc();
        }

        GUILayout.Space(6);
        GUILayout.Label($"HP {stats.Hp.Value}/{stats.MaxHp}   ATK {stats.AttackPower}   DEF {stats.Defense}");

        if (GUILayout.Button("Close  [E]"))
        {
            Close();
        }

        GUILayout.EndArea();
    }

    static GUIStyle richLabel;

    static GUIStyle RichLabel()
    {
        richLabel ??= new GUIStyle(GUI.skin.label) { richText = true };
        return richLabel;
    }
}
