using UnityEngine;

//Contiene todos los eventos que son llamados desde animacion del player y los ejecuta en el PlayerAudio colocado en el Parent.
public class PlayerAudioAnimEvents : MonoBehaviour
{
    private PlayerAudio _playerAudio;

    private void Start()
    {
        _playerAudio = GetComponentInParent<PlayerAudio>();
    }

    public void Audio_Swing()
    {
        Debug.Log("AnimAudioEvent: Swing");
        _playerAudio.Audio_Attack_Swing();
    }
}
