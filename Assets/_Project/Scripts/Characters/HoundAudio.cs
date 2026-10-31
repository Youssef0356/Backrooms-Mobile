using UnityEngine;

/// <summary>
/// Audio loader for the Smily Hound beast sound effects.
/// </summary>
public static class HoundAudio
{
    private static AudioClip growlClip;
    private static AudioClip alertClip;
    private static AudioClip roarClip;
    private static AudioClip biteClip;
    private static AudioClip killScreamClip;
    private static AudioClip[] barkClips;

    public static AudioClip Growl => growlClip ??= Resources.Load<AudioClip>("SFX/Hound/Hound_Growl");
    public static AudioClip Alert => alertClip ??= Resources.Load<AudioClip>("SFX/Hound/Hound_Alert");
    public static AudioClip Roar => roarClip ??= Resources.Load<AudioClip>("SFX/Hound/Hound_Roar");
    public static AudioClip Bite => biteClip ??= Resources.Load<AudioClip>("SFX/Hound/Hound_Bite");
    public static AudioClip KillScream => killScreamClip ??= (
        Resources.Load<AudioClip>("SFX/Hound/Hound_KillScream") ??
        Resources.Load<AudioClip>("SFX/Hound/Hound_Roar")
    );

    public static AudioClip Bark
    {
        get
        {
            if (barkClips == null || barkClips.Length == 0)
            {
                var list = new System.Collections.Generic.List<AudioClip>();
                var b1 = Resources.Load<AudioClip>("SFX/Hound/Hound_Bark");
                var b2 = Resources.Load<AudioClip>("SFX/Hound/Hound_Bark_02");
                var b3 = Resources.Load<AudioClip>("SFX/Hound/Hound_Bark_03");
                if (b1 != null) list.Add(b1);
                if (b2 != null) list.Add(b2);
                if (b3 != null) list.Add(b3);
                barkClips = list.ToArray();
            }
            if (barkClips != null && barkClips.Length > 0)
                return barkClips[Random.Range(0, barkClips.Length)];
            return Roar;
        }
    }
}
