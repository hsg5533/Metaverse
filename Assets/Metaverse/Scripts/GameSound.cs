using UnityEngine;

/// <summary>
/// Every sound in the game, written into memory the first time it is asked for. The project
/// imports no assets, so the clips are generated the same way the models are.
/// </summary>
public static class GameSound
{
    public const int Swing = 0;
    public const int Hit = 1;
    public const int Death = 2;
    public const int LevelUp = 3;
    public const int Pickup = 4;
    public const int Step = 5;
    public const int Growl = 6;
    public const int Warp = 7;
    public const int Chest = 8;
    public const int Jump = 9;
    public const int Land = 10;

    const int Rate = 22050;

    static readonly AudioClip[] Clips = new AudioClip[11];

    /// <summary>Plays out in the world, so distance and direction do the rest.</summary>
    public static void Play(int sound, Vector3 position)
    {
        var clip = Clip(sound);
        if (clip != null)
        {
            AudioSource.PlayClipAtPoint(clip, position);
        }
    }

    /// <summary>The player's own feedback: at the ear, so it never fades with distance.</summary>
    public static void PlayLocal(int sound)
    {
        var camera = Camera.main;
        Play(sound, camera != null ? camera.transform.position : Vector3.zero);
    }

    static AudioClip Clip(int sound)
    {
        if (sound < 0 || sound >= Clips.Length)
        {
            return null;
        }

        // Unity's fake-null: a clip does not survive a play session, so check before reusing.
        if (Clips[sound] == null)
        {
            Clips[sound] = Build(sound);
        }

        return Clips[sound];
    }

    static AudioClip Build(int sound)
    {
        return sound switch
        {
            Swing => Sweep("swing", 0.16f, 1100f, 240f, 0.85f, 0.3f),
            Hit => Sweep("hit", 0.18f, 340f, 90f, 0.35f, 0.5f),
            Death => Sweep("death", 0.5f, 300f, 60f, 0.2f, 0.45f),
            LevelUp => Chime("levelup", new[] { 523f, 659f, 784f, 1047f }, 0.11f, 0.35f),
            Pickup => Chime("pickup", new[] { 784f, 1175f }, 0.07f, 0.3f),
            Step => Sweep("step", 0.07f, 190f, 90f, 0.7f, 0.12f),
            Growl => Sweep("growl", 0.3f, 150f, 70f, 0.45f, 0.4f),
            Warp => Sweep("warp", 0.35f, 200f, 1400f, 0.3f, 0.3f),
            Jump => Sweep("jump", 0.14f, 260f, 620f, 0.25f, 0.25f),
            Land => Sweep("land", 0.12f, 160f, 70f, 0.6f, 0.28f),
            _ => Chime("chest", new[] { 392f, 523f, 784f }, 0.09f, 0.32f),
        };
    }

    /// <summary>
    /// A square wave sliding from one pitch to another, mixed with hiss and fading out.
    /// Square rather than sine because it carries over the rest of the game.
    /// </summary>
    static AudioClip Sweep(string name, float seconds, float startHz, float endHz, float noise, float volume)
    {
        var data = new float[Mathf.CeilToInt(Rate * seconds)];
        var random = new System.Random(name.GetHashCode());
        float phase = 0f;

        for (int i = 0; i < data.Length; i++)
        {
            float t = i / (float)data.Length;
            phase += Mathf.Lerp(startHz, endHz, t) / Rate * 2f * Mathf.PI;

            float wave = Mathf.Sin(phase) > 0f ? 1f : -1f;
            float hiss = (float)(random.NextDouble() * 2.0 - 1.0);
            data[i] = Mathf.Lerp(wave, hiss, noise) * (1f - t) * (1f - t) * volume;
        }

        return Wrap(name, data);
    }

    /// <summary>One note after another, each fading on its own: a little fanfare.</summary>
    static AudioClip Chime(string name, float[] notes, float noteSeconds, float volume)
    {
        int perNote = Mathf.CeilToInt(Rate * noteSeconds);
        var data = new float[perNote * notes.Length];

        for (int note = 0; note < notes.Length; note++)
        {
            float phase = 0f;
            for (int i = 0; i < perNote; i++)
            {
                phase += notes[note] / Rate * 2f * Mathf.PI;
                float fade = 1f - i / (float)perNote;
                data[note * perNote + i] = (Mathf.Sin(phase) > 0f ? 1f : -1f) * fade * fade * volume;
            }
        }

        return Wrap(name, data);
    }

    static AudioClip Wrap(string name, float[] data)
    {
        var clip = AudioClip.Create(name, data.Length, 1, Rate, false);
        clip.SetData(data, 0);
        return clip;
    }
}
