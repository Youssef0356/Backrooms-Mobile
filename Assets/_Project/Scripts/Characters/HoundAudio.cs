using UnityEngine;

/// <summary>
/// Audio loader for the Smily Hound beast sound effects.
/// </summary>
public static class HoundAudio
{
    private static AudioClip growlClip;
    private static AudioClip alertClip;
    private static AudioClip barkClip;
    private static AudioClip roarClip;
    private static AudioClip biteClip;

    public static AudioClip Growl => growlClip ??= Resources.Load<AudioClip>("SFX/Hound/Hound_Growl");
    public static AudioClip Alert => alertClip ??= Resources.Load<AudioClip>("SFX/Hound/Hound_Alert");
    public static AudioClip Bark => barkClip ??= Resources.Load<AudioClip>("SFX/Hound/Hound_Bark");
    public static AudioClip Roar => roarClip ??= Resources.Load<AudioClip>("SFX/Hound/Hound_Roar");
    public static AudioClip Bite => biteClip ??= Resources.Load<AudioClip>("SFX/Hound/Hound_Bite");
}
