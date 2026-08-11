using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Marks water worth fishing in. The lake is scenery with no collider, so this is what tells
/// the rod where it may be cast.
/// </summary>
public class FishingSpot : MonoBehaviour
{
    static readonly List<FishingSpot> All = new();

    /// <summary>The water itself: a float has to come down inside this to catch anything.</summary>
    public float Radius = 17f;

    /// <summary>
    /// The surface: what the builder lays the water at and where a float rests. One number,
    /// or the two drift apart and the float ends up under the ground.
    /// </summary>
    public const float WaterHeight = -0.2f;

    void OnEnable()
    {
        All.Add(this);
    }

    void OnDisable()
    {
        All.Remove(this);
    }

    /// <summary>Standing close enough to cast at all; the bank counts, the next field does not.</summary>
    public static bool NearAny(Vector3 point)
    {
        return Within(point, 14f);
    }

    /// <summary>The float came down on water rather than on the grass beside it.</summary>
    public static bool OnWater(Vector3 point)
    {
        return Within(point, 0f);
    }

    static bool Within(Vector3 point, float extra)
    {
        foreach (var spot in All)
        {
            if (spot == null)
            {
                continue;
            }

            // Flat: the jetty stands above the water and the bed lies below it.
            Vector3 middle = spot.transform.position;
            if (
                new Vector2(point.x - middle.x, point.z - middle.z).magnitude
                <= spot.Radius + extra
            )
            {
                return true;
            }
        }

        return false;
    }
}
