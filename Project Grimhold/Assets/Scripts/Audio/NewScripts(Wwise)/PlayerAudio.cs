using UnityEngine;

public class PlayerAudio : MonoBehaviour
{
    //CHANGE WEAPONS
    //Valores validos para weapon: 
    //ArmingSword, GreatHammer, LongSword, MagicSword, Rapier, 
    //TrainingShortSword, zweihander, Bow
    public void Audio_SetWeapon(string weapon)
{
    // En Wwise setea el switch de arma y le da play al sonido de cambio (aun no).
    AkUnitySoundEngine.PostEvent("SetWeapon_" + weapon, gameObject);
}

    void Start()
    {
        //Para test. Borrar.
        AkUnitySoundEngine.SetSwitch("Weapon", "ArmingSword", gameObject);
    }
    //PLAYER ATTACKS

    //Swing: llamar desde animacion.
    public void Audio_Attack_Swing_Main()
{
    AkUnitySoundEngine.PostEvent("Attack_Swing_Main", gameObject);
    Debug.Log("AudioSwingMain");
}

    public void Audio_Attack_Swing_Off()
{
    AkUnitySoundEngine.PostEvent("Attack_Swing_Off", gameObject);
    Debug.Log("AudioSwingOff");
}

    //Llamar por desde el ataque y entregar el AudioSurface que se obtiene del componente AudioProperties del objeto golpeado.
    public void Audio_Attack_Hit(string AudioSurface)
{
    AkUnitySoundEngine.SetSwitch("Material", AudioSurface, gameObject);
    AkUnitySoundEngine.PostEvent("Attack_Hit", gameObject);
}

  
}
