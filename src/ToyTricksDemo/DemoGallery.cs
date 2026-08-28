using System;
using System.Collections.Generic;
using LabApi.Features.Wrappers;
using MEC;
using ToyTricksDemo.Exhibits;
using UnityEngine;
using Logger = LabApi.Features.Console.Logger;

namespace ToyTricksDemo;

/// <summary>
/// The spawned showcase: an invisible root placed on the floor in front of the viewer (root +Z = the
/// viewer's facing, so every TextToy with a local identity rotation is readable from where the viewer
/// stands), with up to seven exhibit bays parented under it. One gallery exists at a time; respawning
/// tears the previous one down. The static bays cost zero per-frame traffic; the only live senders are
/// the 1 Hz Arguments update on the text bay, 30 Hz strike-array transforms and light pulses, the
/// 15 Hz waypoint carrier, the firefight bay's two 15 Hz carriers, and the patrol's 15 Hz walker tick.
/// </summary>
internal sealed class DemoGallery
{
    private const float BaySpacing = 4.6f;

    private static DemoGallery? _current;

    private readonly List<AdminToy> _bag = new();
    private TextToy? _liveText;
    private GlowExhibit? _glowExhibit;
    private StrikeArrayAltExhibit? _strikeAltExhibit;
    private WaypointExhibit? _waypointExhibit;
    private FirefightExhibit? _firefightExhibit;
    private PatrolExhibit? _patrolExhibit;
    private CoroutineHandle _tick;
    private float _spawnedAt;

    private DemoGallery()
    {
    }

    /// <summary>Spawns the requested exhibit(s) in front of <paramref name="viewer"/>. Returns a summary line.</summary>
    public static string SpawnFor(Player viewer, string selection, float distanceMeters)
    {
        Clear();

        Quaternion yaw = Quaternion.Euler(0f, viewer.Rotation.eulerAngles.y, 0f);
        Vector3 center = viewer.Position + (yaw * Vector3.forward * distanceMeters);
        // Snap the gallery to the floor under the target point (falls back to ~feet height on a miss).
        Vector3 floor = Physics.Raycast(center + (Vector3.up * 0.5f), Vector3.down, out RaycastHit hit, 6f)
            ? hit.point
            : center + (Vector3.down * 0.9f);

        DemoGallery gallery = new();
        try
        {
            PrimitiveObjectToy root = ExhibitTools.InvisibleParent(floor, yaw, Vector3.one, null, gallery._bag);
            Transform t = root.Transform;

            bool all = selection == "all";
            if (all || selection == "shear")
            {
                // The shear bay grew a fifth piece on its right (the 3D shard); pull it an extra
                // 0.85 m left in the combined layout so its plinth keeps clear air to the glow bay.
                ShearExhibit.Build(t, new Vector3(all ? -BaySpacing - 0.85f : 0f, 0f, 0f), gallery._bag);
            }

            if (all || selection == "glow")
            {
                gallery._glowExhibit = GlowExhibit.Build(t, Vector3.zero, gallery._bag);
            }

            if (all || selection == "strikealt")
            {
                // Behind the glow bay when spawning everything; front and center when spawned alone.
                gallery._strikeAltExhibit = StrikeArrayAltExhibit.Build(
                    t, all ? new Vector3(0f, 0f, BaySpacing) : Vector3.zero, gallery._bag);
            }

            if (all || selection == "text")
            {
                gallery._liveText = WorldTextExhibit.Build(t, new Vector3(all ? BaySpacing : 0f, 0f, 0f), gallery._bag);
            }

            if (all || selection == "waypoint")
            {
                gallery._waypointExhibit = WaypointExhibit.Build(
                    t, new Vector3(all ? BaySpacing * 2f : 0f, 0f, 1.2f), gallery._bag);
            }

            if (all || selection == "firefight")
            {
                // The firefight bay is DEEP (two lanes 7.5 m apart) and its shooter fires live rounds
                // down the far lane, so in the combined layout it goes well behind everything else —
                // never beside a bay a viewer might be standing in.
                gallery._firefightExhibit = FirefightExhibit.Build(
                    t, all ? new Vector3(0f, 0f, BaySpacing * 2.6f) : Vector3.zero, gallery._bag);
            }

            if (all || selection == "patrol")
            {
                // Directly BEHIND the firefight bay (its far lane is at +7.5), so the two shooting bays
                // read as one deep range rather than overlapping. Its own targets sit further downrange
                // again, all pointing away from where a viewer stands.
                gallery._patrolExhibit = PatrolExhibit.Build(
                    t, all ? new Vector3(0f, 0f, (BaySpacing * 2.6f) + 15f) : Vector3.zero, gallery._bag);
            }
        }
        catch (Exception exception)
        {
            gallery.Destroy();
            Logger.Error($"[ToyTricksDemo] Gallery spawn failed: {exception}");
            return $"Gallery spawn failed: {exception.GetBaseException().Message}";
        }

        gallery._spawnedAt = Time.timeSinceLevelLoad;
        if (gallery._liveText != null)
        {
            gallery._tick = Timing.RunCoroutine(gallery.LiveTextLoop());
        }

        gallery._glowExhibit?.Start();
        gallery._strikeAltExhibit?.Start();
        gallery._waypointExhibit?.Start();
        gallery._firefightExhibit?.Start();
        gallery._patrolExhibit?.Start();
        _current = gallery;
        return $"Spawned '{selection}' ({gallery._bag.Count} toys) {distanceMeters:0.#} m ahead. 'toytricks clear' removes it.";
    }

    /// <summary>Destroys the current gallery, if any. Returns true when something was removed.</summary>
    public static bool Clear()
    {
        DemoGallery? gallery = _current;
        _current = null;
        if (gallery == null)
        {
            return false;
        }

        gallery.Destroy();
        return true;
    }

    /// <summary>1 Hz uptime counter through the Arguments SyncList — a per-element delta, not a TextFormat resend.</summary>
    private IEnumerator<float> LiveTextLoop()
    {
        while (_liveText != null && !_liveText.IsDestroyed)
        {
            int seconds = Mathf.FloorToInt(Time.timeSinceLevelLoad - _spawnedAt);
            _liveText.Arguments[0] = seconds.ToString();
            yield return Timing.WaitForSeconds(1f);
        }
    }

    private void Destroy()
    {
        Timing.KillCoroutines(_tick);
        _liveText = null;

        // Stop the animators before tearing down the toys they drive.
        _glowExhibit?.Stop();
        _glowExhibit = null;
        _strikeAltExhibit?.Stop();
        _strikeAltExhibit = null;

        // The waypoint/firefight bays' dummies, pickups, and ragdolls are not AdminToys (not in the
        // bag) and must go first, while their carriers still exist, so nothing is left standing on a
        // destroyed platform.
        _waypointExhibit?.StopAndDestroyPayloads();
        _waypointExhibit = null;
        _firefightExhibit?.StopAndDestroyPayloads();
        _firefightExhibit = null;
        _patrolExhibit?.StopAndDestroyPayloads();
        _patrolExhibit = null;

        // Children first (reverse spawn order), so nothing is double-destroyed by a parent cascade.
        for (int i = _bag.Count - 1; i >= 0; i--)
        {
            try
            {
                if (_bag[i] != null && !_bag[i].IsDestroyed)
                {
                    _bag[i].Destroy();
                }
            }
            catch (Exception exception)
            {
                Logger.Warn($"[ToyTricksDemo] Toy teardown failed: {exception.GetBaseException().Message}");
            }
        }

        _bag.Clear();
    }
}
