using UnityEngine;

public class AudioProperties : MonoBehaviour
{
    public AudioSurface audioSurface;
}

public enum AudioSurface
{
    Wall,
    Cloth,
    Leather,
    LeatherCloth,
    Plate,
    ChainMail,
    Shield
}